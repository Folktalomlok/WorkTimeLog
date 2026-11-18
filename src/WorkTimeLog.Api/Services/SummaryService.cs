using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;

namespace WorkTimeLog.Api.Services;

public class SummaryService
{
    private readonly AppDbContext _db;

    public SummaryService(AppDbContext db) => _db = db;

    /// <summary>สรุปรายวัน: เวลารวม (ไม่นับรายการยกเลิก), เวลาที่เหลือจาก 480, จำนวนรายการ</summary>
    public async Task<DailySummaryDto> GetDailyAsync(DateOnly date)
    {
        var rows = await _db.TimeEntries
            .Where(e => e.WorkDate == date)
            .Select(e => new { e.Minutes, e.IsCancelled })
            .ToListAsync();

        var total = rows.Where(r => !r.IsCancelled).Sum(r => r.Minutes);
        return new DailySummaryDto(
            date,
            total,
            Math.Max(0, TimeEntryService.MaxDailyMinutes - total),
            TimeEntryService.MaxDailyMinutes,
            rows.Count(r => !r.IsCancelled),
            rows.Count(r => r.IsCancelled));
    }

    /// <summary>เวลารวมแยกตามโครงการในช่วงวันที่ (ไม่นับรายการยกเลิก)</summary>
    public async Task<ProjectSummaryDto> GetByProjectAsync(DateOnly from, DateOnly to)
    {
        TimeEntryService.EnsureValidRange(from, to);

        var items = await _db.TimeEntries
            .Where(e => !e.IsCancelled && e.WorkDate >= from && e.WorkDate <= to)
            .GroupBy(e => new { e.ProjectId, e.Project!.Name })
            .Select(g => new ProjectSummaryItemDto(g.Key.ProjectId, g.Key.Name, g.Sum(x => x.Minutes), g.Count()))
            .ToListAsync();

        var ordered = items.OrderByDescending(i => i.TotalMinutes).ThenBy(i => i.ProjectName).ToList();
        return new ProjectSummaryDto(from, to, ordered.Sum(i => i.TotalMinutes), ordered);
    }
}
