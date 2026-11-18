using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Tests;

public class TimeEntryServiceTests : IDisposable
{
    // ใช้วันที่ที่ไม่มี seed data เพื่อไม่ให้ข้อมูลตัวอย่างปนกับการทดสอบ
    private static readonly DateOnly Day = new(2030, 1, 10);
    private static readonly DateOnly OtherDay = new(2030, 1, 11);
    private const int ProjectId = 1;

    private readonly TestDb _db = new();
    private readonly TimeEntryService _service;

    public TimeEntryServiceTests() => _service = new TimeEntryService(_db.Context);

    public void Dispose() => _db.Dispose();

    private Task<TimeEntryDto> Add(DateOnly date, int minutes, string desc = "งานทดสอบ") =>
        _service.CreateAsync(Entry(date, minutes, desc));

    private static TimeEntryUpsertDto Entry(DateOnly date, int minutes, string desc = "งานทดสอบ") =>
        new() { WorkDate = date, ProjectId = ProjectId, Description = desc, Minutes = minutes };

    // ---------------- กรณีทดสอบที่กำหนด ----------------

    [Fact(DisplayName = "1. เพิ่มรายการให้รวมได้พอดี 480 นาที → ผ่าน")]
    public async Task Create_TotalExactly480_Succeeds()
    {
        await Add(Day, 200);
        await Add(Day, 280);

        Assert.Equal(480, await _service.GetDailyTotalAsync(Day));
    }

    [Fact(DisplayName = "2. เพิ่มรายการจนรวมได้ 481 นาที → ไม่ผ่าน")]
    public async Task Create_Total481_Fails()
    {
        await Add(Day, 400);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Add(Day, 81));
        Assert.Equal("เวลารวมวันที่ 2030-01-10 จะเป็น 481 นาที เกินกำหนด 480 นาที (เหลือได้อีก 80 นาที)", ex.Message);
        Assert.Equal(400, await _service.GetDailyTotalAsync(Day));
    }

    [Fact(DisplayName = "3. รายการยกเลิก 300 + รายการปกติ 400 นาที → ผ่าน (ไม่นับรายการยกเลิก)")]
    public async Task CancelledEntry_IsNotCounted()
    {
        var cancelled = await Add(Day, 300);
        await _service.CancelAsync(cancelled.Id);

        var created = await Add(Day, 400);

        Assert.False(created.IsCancelled);
        Assert.Equal(400, await _service.GetDailyTotalAsync(Day));
    }

    [Fact(DisplayName = "4. แก้ไขจาก 200 เป็น 280 ในวันที่มีรายการอื่น 200 นาที → ผ่าน (รวม 480 ไม่ใช่ 680)")]
    public async Task Update_ReplacesOldValue_Total480_Succeeds()
    {
        await Add(Day, 200, "รายการอื่น");
        var target = await Add(Day, 200, "รายการที่แก้ไข");

        var updated = await _service.UpdateAsync(target.Id, Entry(Day, 280, "รายการที่แก้ไข"));

        Assert.Equal(280, updated.Minutes);
        Assert.Equal(480, await _service.GetDailyTotalAsync(Day));
    }

    [Fact(DisplayName = "5. แก้ไขจาก 200 เป็น 281 ในวันที่มีรายการอื่น 200 นาที → ไม่ผ่าน")]
    public async Task Update_Total481_Fails()
    {
        await Add(Day, 200, "รายการอื่น");
        var target = await Add(Day, 200, "รายการที่แก้ไข");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _service.UpdateAsync(target.Id, Entry(Day, 281)));

        Assert.Contains("481 นาที", ex.Message);
        Assert.Contains("เหลือได้อีก 280 นาที", ex.Message);
        Assert.Equal(200, (await _service.GetAsync(target.Id)).Minutes); // ค่าเดิมไม่เปลี่ยน
    }

    [Fact(DisplayName = "6. แก้ไขโดยย้ายไปวันที่ที่เต็มแล้ว → ไม่ผ่าน")]
    public async Task Update_MoveToFullDay_Fails()
    {
        await Add(OtherDay, 480, "วันนี้เต็มแล้ว");
        var target = await Add(Day, 60);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _service.UpdateAsync(target.Id, Entry(OtherDay, 60)));

        Assert.Contains("2030-01-11", ex.Message);
        Assert.Contains("540 นาที", ex.Message);
        Assert.Equal(Day, (await _service.GetAsync(target.Id)).WorkDate); // ยังอยู่วันเดิม
    }

    [Fact(DisplayName = "7. กู้คืนรายการที่ยกเลิกแล้วทำให้เกิน 480 → ไม่ผ่าน")]
    public async Task Restore_ExceedingLimit_Fails()
    {
        var old = await Add(Day, 300);
        await _service.CancelAsync(old.Id);
        await Add(Day, 400);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _service.RestoreAsync(old.Id));

        Assert.StartsWith("ไม่สามารถกู้คืนได้", ex.Message);
        Assert.Contains("700 นาที", ex.Message);
        Assert.True((await _service.GetAsync(old.Id)).IsCancelled);
    }

    [Theory(DisplayName = "8. Minutes = 0 หรือติดลบ → ไม่ผ่าน")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public async Task Create_ZeroOrNegativeMinutes_Fails(int minutes)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Add(Day, minutes));
        Assert.Equal("เวลาที่ใช้ต้องมากกว่า 0 นาที", ex.Message);
    }

    // ---------------- กรณีทดสอบเพิ่มเติม ----------------

    [Fact(DisplayName = "9. Minutes เกิน 480 ในรายการเดียว → ไม่ผ่าน")]
    public async Task Create_MinutesOver480_Fails()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Add(Day, 481));
        Assert.Equal("เวลาที่ใช้ต่อรายการต้องไม่เกิน 480 นาที", ex.Message);
    }

    [Fact(DisplayName = "10. แก้ไข Minutes เป็น 0 → ไม่ผ่าน")]
    public async Task Update_ZeroMinutes_Fails()
    {
        var target = await Add(Day, 60);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateAsync(target.Id, Entry(Day, 0)));
    }

    [Fact(DisplayName = "11. แก้ไขย้ายไปวันที่ยังว่าง → ผ่าน และวันเดิมลดลง")]
    public async Task Update_MoveToDayWithRoom_Succeeds()
    {
        await Add(OtherDay, 300);
        var target = await Add(Day, 180);

        var updated = await _service.UpdateAsync(target.Id, Entry(OtherDay, 180));

        Assert.Equal(OtherDay, updated.WorkDate);
        Assert.Equal(0, await _service.GetDailyTotalAsync(Day));
        Assert.Equal(480, await _service.GetDailyTotalAsync(OtherDay));
    }

    [Fact(DisplayName = "12. กู้คืนรายการที่ยังไม่เกิน 480 → ผ่าน")]
    public async Task Restore_WithinLimit_Succeeds()
    {
        var old = await Add(Day, 200);
        await _service.CancelAsync(old.Id);
        await Add(Day, 280);

        var restored = await _service.RestoreAsync(old.Id);

        Assert.False(restored.IsCancelled);
        Assert.Equal(480, await _service.GetDailyTotalAsync(Day));
    }

    [Fact(DisplayName = "13. ยกเลิกเป็น soft cancel — ข้อมูลยังอยู่ในฐานข้อมูล")]
    public async Task Cancel_IsSoftDelete()
    {
        var e = await Add(Day, 100);
        await _service.CancelAsync(e.Id);

        var list = await _service.GetByDateAsync(Day);
        Assert.Single(list);
        Assert.True(list[0].IsCancelled);
    }

    [Fact(DisplayName = "14. แก้ไขรายการที่ยกเลิกแล้ว → ไม่ผ่าน")]
    public async Task Update_CancelledEntry_Fails()
    {
        var e = await Add(Day, 100);
        await _service.CancelAsync(e.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateAsync(e.Id, Entry(Day, 50)));
    }

    [Fact(DisplayName = "15. รายละเอียดงานว่าง / ยาวเกิน 500 → ไม่ผ่าน")]
    public async Task Create_InvalidDescription_Fails()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Add(Day, 30, "   "));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Add(Day, 30, new string('ก', 501)));
    }

    [Fact(DisplayName = "16. แก้ไข/ยกเลิกรายการที่ไม่มีอยู่ → NotFound")]
    public async Task MissingEntry_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(9999, Entry(Day, 30)));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CancelAsync(9999));
    }
}
