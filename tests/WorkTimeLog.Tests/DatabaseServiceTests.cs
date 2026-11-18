using System.Text;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Tests;

public class DatabaseServiceTests : IDisposable
{
    private static readonly DateOnly Day = new(2030, 3, 1);
    private readonly TestDb _db = new();
    private readonly TimeEntryService _entries;
    private readonly DatabaseService _service;

    public DatabaseServiceTests()
    {
        _entries = new TimeEntryService(_db.Context);
        _service = new DatabaseService(_db.Context);
    }

    public void Dispose() => _db.Dispose();

    private Task<TimeEntryDto> Add(DateOnly date, int minutes, string desc = "งานทดสอบ") =>
        _entries.CreateAsync(new TimeEntryUpsertDto { WorkDate = date, ProjectId = 1, Description = desc, Minutes = minutes });

    [Fact(DisplayName = "21. daily-totals ไม่นับรายการที่ยกเลิก")]
    public async Task DailyTotals_ExcludeCancelled()
    {
        await Add(Day, 100);
        var cancelled = await Add(Day, 300);
        await _entries.CancelAsync(cancelled.Id);

        var day = (await _service.GetDailyTotalsAsync()).Single(d => d.Date == Day);

        Assert.Equal(100, day.TotalMinutes);
        Assert.Equal(380, day.RemainingMinutes);
        Assert.Equal(1, day.EntryCount);
        Assert.Equal(1, day.CancelledCount);
        Assert.False(day.IsOverLimit);
    }

    [Fact(DisplayName = "22. entries คืนรายการที่ยกเลิกด้วย พร้อมชื่อโครงการ เรียงวันล่าสุดก่อน")]
    public async Task Entries_IncludeCancelled_NewestFirst()
    {
        var cancelled = await Add(Day, 60, "รายการที่ยกเลิก");
        await _entries.CancelAsync(cancelled.Id);

        var list = await _service.GetEntriesAsync();

        var row = Assert.Single(list, e => e.Id == cancelled.Id);
        Assert.True(row.IsCancelled);
        Assert.Equal("โครงงานคอมพิวเตอร์", row.ProjectName);
        Assert.Equal(Day, list[0].WorkDate); // 2030 ใหม่กว่า seed data (2026)
        Assert.Equal(list.OrderByDescending(e => e.WorkDate).Select(e => e.WorkDate), list.Select(e => e.WorkDate));
    }

    [Fact(DisplayName = "23. daily-totals แสดงสถานะเกิน 480 เมื่อข้อมูลในฐานข้อมูลเกิน")]
    public async Task DailyTotals_FlagsOverLimit()
    {
        // ใส่ข้อมูลตรงเข้า DbContext (ข้าม service) เพื่อจำลองข้อมูลที่เกินกำหนด
        var now = DateTime.UtcNow;
        _db.Context.TimeEntries.AddRange(
            new() { WorkDate = Day, ProjectId = 1, Description = "a", Minutes = 300, CreatedAt = now, UpdatedAt = now },
            new() { WorkDate = Day, ProjectId = 2, Description = "b", Minutes = 200, CreatedAt = now, UpdatedAt = now });
        await _db.Context.SaveChangesAsync();

        var day = (await _service.GetDailyTotalsAsync()).Single(d => d.Date == Day);

        Assert.Equal(500, day.TotalMinutes);
        Assert.Equal(0, day.RemainingMinutes);
        Assert.True(day.IsOverLimit);
    }

    [Fact(DisplayName = "24. info นับจำนวนแถวและเวลารวมถูกต้อง (seed data)")]
    public async Task Info_CountsSeedData()
    {
        var info = await _service.GetInfoAsync();

        Assert.Equal("SQLite", info.DatabaseType);
        Assert.False(info.CanDownloadFile); // in-memory ไม่มีไฟล์ให้ดาวน์โหลด
        Assert.Null(info.Host);
        Assert.Equal(3, info.ProjectCount);
        Assert.Equal(5, info.EntryCount);
        Assert.Equal(1, info.CancelledCount);
        Assert.Equal(450, info.TotalMinutes); // 120 + 60 + 180 + 90 (ไม่นับ 90 ที่ยกเลิก)
        Assert.Contains(info.Tables, t => t.Name == "TimeEntries" && t.Rows == 5);
        Assert.Contains(info.Tables, t => t.Name == "Projects" && t.Rows == 3);
    }

    [Fact(DisplayName = "25. CSV เป็น UTF-8 with BOM และ escape เครื่องหมาย , \" และสูตร Excel")]
    public async Task Csv_HasBomAndEscapes()
    {
        await Add(Day, 30, "ประชุม, สรุป \"งาน\"");
        await Add(Day, 30, "=1+1");

        var bytes = await _service.ExportEntriesCsvAsync();

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("Id,WorkDate,ProjectId,ProjectName,Description,Minutes,Note,IsCancelled,CreatedAt,UpdatedAt", text);
        Assert.Contains("\"ประชุม, สรุป \"\"งาน\"\"\"", text);
        Assert.Contains("\"'=1+1\"", text);
    }
}
