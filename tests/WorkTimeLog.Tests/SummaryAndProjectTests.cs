using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Tests;

public class SummaryAndProjectTests : IDisposable
{
    private static readonly DateOnly Day = new(2030, 2, 1);
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact(DisplayName = "17. สรุปรายวันไม่นับรายการยกเลิก และคำนวณเวลาที่เหลือ")]
    public async Task DailySummary_ExcludesCancelled()
    {
        var entries = new TimeEntryService(_db.Context);
        await entries.CreateAsync(new() { WorkDate = Day, ProjectId = 1, Description = "A", Minutes = 120 });
        var b = await entries.CreateAsync(new() { WorkDate = Day, ProjectId = 2, Description = "B", Minutes = 60 });
        await entries.CancelAsync(b.Id);

        var s = await new SummaryService(_db.Context).GetDailyAsync(Day);

        Assert.Equal(120, s.TotalMinutes);
        Assert.Equal(360, s.RemainingMinutes);
        Assert.Equal(1, s.EntryCount);
        Assert.Equal(1, s.CancelledCount);
    }

    [Fact(DisplayName = "18. สรุปแยกตามโครงการในช่วงวันที่")]
    public async Task ByProjectSummary_GroupsCorrectly()
    {
        var entries = new TimeEntryService(_db.Context);
        await entries.CreateAsync(new() { WorkDate = Day, ProjectId = 1, Description = "A", Minutes = 100 });
        await entries.CreateAsync(new() { WorkDate = Day.AddDays(1), ProjectId = 1, Description = "A2", Minutes = 50 });
        await entries.CreateAsync(new() { WorkDate = Day, ProjectId = 2, Description = "B", Minutes = 30 });
        await entries.CreateAsync(new() { WorkDate = Day.AddDays(10), ProjectId = 2, Description = "นอกช่วง", Minutes = 30 });

        var s = await new SummaryService(_db.Context).GetByProjectAsync(Day, Day.AddDays(1));

        Assert.Equal(180, s.TotalMinutes);
        Assert.Equal(2, s.Items.Count);
        Assert.Equal(1, s.Items[0].ProjectId);
        Assert.Equal(150, s.Items[0].TotalMinutes);
        Assert.Equal(2, s.Items[0].EntryCount);
    }

    [Fact(DisplayName = "19. ชื่อโครงการซ้ำ (ไม่สนตัวพิมพ์) → ไม่ผ่าน")]
    public async Task Project_DuplicateName_Fails()
    {
        var projects = new ProjectService(_db.Context);
        await projects.CreateAsync(new ProjectCreateDto { Name = "Web App" });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => projects.CreateAsync(new ProjectCreateDto { Name = "  web app " }));
        Assert.Contains("มีโครงการชื่อ", ex.Message);
    }

    [Fact(DisplayName = "20. ช่วงวันที่กลับด้าน (to < from) → ไม่ผ่าน")]
    public async Task Range_Inverted_Fails()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => new SummaryService(_db.Context).GetByProjectAsync(Day, Day.AddDays(-1)));
    }
}
