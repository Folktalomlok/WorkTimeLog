using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Services;

/// <summary>
/// รวม logic และกติกาทางธุรกิจของการบันทึกเวลาทั้งหมด (แยกจาก Controller เพื่อให้ทดสอบได้)
/// </summary>
public class TimeEntryService
{
    public const int MaxDailyMinutes = 480;
    public const int MaxTextLength = 500;

    private readonly AppDbContext _db;

    public TimeEntryService(AppDbContext db) => _db = db;

    // ---------------- Queries ----------------

    public async Task<TimeEntryDto> GetAsync(int id)
    {
        var entry = await _db.TimeEntries.Include(e => e.Project).FirstOrDefaultAsync(e => e.Id == id)
                    ?? throw NotFound(id);
        return entry.ToDto();
    }

    public async Task<List<TimeEntryDto>> GetByDateAsync(DateOnly date)
    {
        var list = await _db.TimeEntries.Include(e => e.Project)
            .Where(e => e.WorkDate == date)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync();
        return list.Select(e => e.ToDto()).ToList();
    }

    public async Task<List<TimeEntryDto>> GetRangeAsync(DateOnly from, DateOnly to)
    {
        EnsureValidRange(from, to);
        var list = await _db.TimeEntries.Include(e => e.Project)
            .Where(e => e.WorkDate >= from && e.WorkDate <= to)
            .OrderBy(e => e.WorkDate).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync();
        return list.Select(e => e.ToDto()).ToList();
    }

    /// <summary>
    /// เวลารวมของวันนั้น เฉพาะรายการที่ไม่ถูกยกเลิก (กติกาข้อ 2)
    /// excludeEntryId = รายการที่กำลังแก้ไข/กู้คืน ซึ่งต้องไม่นับค่าเดิม (กติกาข้อ 3)
    /// </summary>
    public Task<int> GetDailyTotalAsync(DateOnly date, int? excludeEntryId = null) =>
        _db.TimeEntries
            .Where(e => e.WorkDate == date && !e.IsCancelled)
            .Where(e => excludeEntryId == null || e.Id != excludeEntryId)
            .SumAsync(e => e.Minutes);

    // ---------------- Commands ----------------

    public async Task<TimeEntryDto> CreateAsync(TimeEntryUpsertDto dto)
    {
        var v = await ValidateAsync(dto, currentProjectId: null);
        await EnsureWithinDailyLimitAsync(v.WorkDate, v.Minutes, excludeEntryId: null);

        var now = DateTime.UtcNow;
        var entry = new TimeEntry
        {
            WorkDate = v.WorkDate,
            ProjectId = v.ProjectId,
            Description = v.Description,
            Minutes = v.Minutes,
            Note = v.Note,
            IsCancelled = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.TimeEntries.Add(entry);
        await _db.SaveChangesAsync();
        await _db.Entry(entry).Reference(e => e.Project).LoadAsync();
        return entry.ToDto();
    }

    public async Task<TimeEntryDto> UpdateAsync(int id, TimeEntryUpsertDto dto)
    {
        var entry = await _db.TimeEntries.FindAsync(id) ?? throw NotFound(id);
        if (entry.IsCancelled)
            throw new BusinessRuleException("ไม่สามารถแก้ไขรายการที่ถูกยกเลิกแล้ว กรุณากู้คืนรายการก่อน");

        var v = await ValidateAsync(dto, currentProjectId: entry.ProjectId);

        // กติกาข้อ 3: รวม = รายการอื่นในวันใหม่ (ไม่นับตัวเอง) + ค่า Minutes ใหม่
        // ถ้าย้ายวันที่ ตรวจกับวันที่ใหม่ (v.WorkDate) โดยอัตโนมัติ
        await EnsureWithinDailyLimitAsync(v.WorkDate, v.Minutes, excludeEntryId: entry.Id);

        entry.WorkDate = v.WorkDate;
        entry.ProjectId = v.ProjectId;
        entry.Description = v.Description;
        entry.Minutes = v.Minutes;
        entry.Note = v.Note;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.Entry(entry).Reference(e => e.Project).LoadAsync();
        return entry.ToDto();
    }

    /// <summary>ยกเลิกแบบ soft cancel (ไม่ลบจริง) — กติกาข้อ 4</summary>
    public async Task<TimeEntryDto> CancelAsync(int id)
    {
        var entry = await _db.TimeEntries.FindAsync(id) ?? throw NotFound(id);
        if (entry.IsCancelled)
            throw new BusinessRuleException("รายการนี้ถูกยกเลิกไปแล้ว");

        entry.IsCancelled = true;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.Entry(entry).Reference(e => e.Project).LoadAsync();
        return entry.ToDto();
    }

    /// <summary>กู้คืนรายการที่ยกเลิก ต้องตรวจกติกา 480 นาทีอีกครั้ง — กติกาข้อ 4</summary>
    public async Task<TimeEntryDto> RestoreAsync(int id)
    {
        var entry = await _db.TimeEntries.FindAsync(id) ?? throw NotFound(id);
        if (!entry.IsCancelled)
            throw new BusinessRuleException("รายการนี้ไม่ได้ถูกยกเลิก จึงไม่ต้องกู้คืน");

        await EnsureWithinDailyLimitAsync(entry.WorkDate, entry.Minutes, excludeEntryId: entry.Id,
            prefix: "ไม่สามารถกู้คืนได้: ");

        entry.IsCancelled = false;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.Entry(entry).Reference(e => e.Project).LoadAsync();
        return entry.ToDto();
    }

    // ---------------- Rules ----------------

    /// <summary>กติกาข้อ 1: เวลารวมต่อวันต้องไม่เกิน 480 นาที</summary>
    public async Task EnsureWithinDailyLimitAsync(DateOnly date, int newMinutes, int? excludeEntryId, string prefix = "")
    {
        var others = await GetDailyTotalAsync(date, excludeEntryId);
        var total = others + newMinutes;
        if (total > MaxDailyMinutes)
        {
            var remaining = Math.Max(0, MaxDailyMinutes - others);
            throw new BusinessRuleException(
                $"{prefix}เวลารวมวันที่ {FormatDate(date)} จะเป็น {total} นาที เกินกำหนด {MaxDailyMinutes} นาที (เหลือได้อีก {remaining} นาที)");
        }
    }

    /// <summary>กติกาข้อ 5: Minutes ต้องมากกว่า 0 และไม่เกิน 480</summary>
    public static void ValidateMinutes(int? minutes)
    {
        if (minutes is null)
            throw new BusinessRuleException("กรุณาระบุเวลาที่ใช้ (นาที)");
        if (minutes <= 0)
            throw new BusinessRuleException("เวลาที่ใช้ต้องมากกว่า 0 นาที");
        if (minutes > MaxDailyMinutes)
            throw new BusinessRuleException($"เวลาที่ใช้ต่อรายการต้องไม่เกิน {MaxDailyMinutes} นาที");
    }

    private record ValidEntry(DateOnly WorkDate, int ProjectId, string Description, int Minutes, string? Note);

    private async Task<ValidEntry> ValidateAsync(TimeEntryUpsertDto? dto, int? currentProjectId)
    {
        if (dto is null)
            throw new BusinessRuleException("ไม่พบข้อมูลที่ส่งมา");
        if (dto.WorkDate is null)
            throw new BusinessRuleException("กรุณาระบุวันที่");
        if (dto.ProjectId is null or <= 0)
            throw new BusinessRuleException("กรุณาเลือกโครงการ");

        var description = dto.Description?.Trim() ?? string.Empty;
        if (description.Length == 0)
            throw new BusinessRuleException("กรุณากรอกรายละเอียดงาน");
        if (description.Length > MaxTextLength)
            throw new BusinessRuleException($"รายละเอียดงานต้องยาวไม่เกิน {MaxTextLength} ตัวอักษร");

        var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        if (note is { Length: > MaxTextLength })
            throw new BusinessRuleException($"หมายเหตุต้องยาวไม่เกิน {MaxTextLength} ตัวอักษร");

        ValidateMinutes(dto.Minutes);

        var project = await _db.Projects.FindAsync(dto.ProjectId.Value)
                      ?? throw new BusinessRuleException("ไม่พบโครงการที่เลือก");
        // ไม่ให้เลือกโครงการที่ปิดใช้งานแล้ว (ยกเว้นรายการเดิมที่ใช้โครงการนั้นอยู่แล้ว)
        if (!project.IsActive && project.Id != currentProjectId)
            throw new BusinessRuleException($"โครงการ \"{project.Name}\" ถูกปิดใช้งานแล้ว");

        return new ValidEntry(dto.WorkDate.Value, project.Id, description, dto.Minutes!.Value, note);
    }

    internal static void EnsureValidRange(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new BusinessRuleException("วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่มต้น");
    }

    public static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // กันปฏิทินพุทธศักราชจาก culture th-TH

    private static NotFoundException NotFound(int id) => new($"ไม่พบรายการบันทึกเวลาเลขที่ {id}");
}
