using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Data;

/// <summary>ใส่ข้อมูลตัวอย่าง เฉพาะเมื่อตาราง Projects และ TimeEntries ยังว่างทั้งคู่</summary>
public static class DbSeeder
{
    public static async Task<bool> SeedIfEmptyAsync(AppDbContext db)
    {
        if (await db.Projects.AnyAsync() || await db.TimeEntries.AnyAsync())
            return false;

        var p1 = new Project { Name = "โครงงานคอมพิวเตอร์", IsActive = true };
        var p2 = new Project { Name = "ฝึกงาน / สหกิจศึกษา", IsActive = true };
        var p3 = new Project { Name = "ทบทวนบทเรียนและการบ้าน", IsActive = true };
        db.Projects.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var d1 = new DateOnly(2026, 11, 17);
        var d2 = new DateOnly(2026, 11, 18);
        var t1 = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

        db.TimeEntries.AddRange(
            new TimeEntry { WorkDate = d1, Project = p1, Description = "ออกแบบฐานข้อมูลและ ER Diagram", Minutes = 120, CreatedAt = t1, UpdatedAt = t1 },
            new TimeEntry { WorkDate = d1, Project = p2, Description = "ประชุมทีมประจำสัปดาห์", Minutes = 60, Note = "ประชุมออนไลน์", CreatedAt = t1, UpdatedAt = t1 },
            new TimeEntry { WorkDate = d1, Project = p3, Description = "ทำแบบฝึกหัดคณิตศาสตร์", Minutes = 90, Note = "ตัวอย่างรายการที่ถูกยกเลิก", IsCancelled = true, CreatedAt = t1, UpdatedAt = t1 },
            new TimeEntry { WorkDate = d2, Project = p1, Description = "เขียน Web API ด้วย ASP.NET Core", Minutes = 180, CreatedAt = t2, UpdatedAt = t2 },
            new TimeEntry { WorkDate = d2, Project = p2, Description = "จัดทำเอกสารรายงานประจำวัน", Minutes = 90, Note = "รออาจารย์ตรวจ", CreatedAt = t2, UpdatedAt = t2 });
        await db.SaveChangesAsync();
        return true;
    }
}
