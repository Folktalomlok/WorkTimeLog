using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace WorkTimeLog.Api.Data;

/// <summary>
/// เตรียมฐานข้อมูลตอนเริ่มแอป แบบเร็วสำหรับ serverless (Vercel cold start):
/// - ถามรายการ migration ที่ยังไม่ได้ apply (1 query ไปที่ __EFMigrationsHistory)
///   ถ้าไม่มีเลย → ข้าม Migrate() ทั้งหมด
/// - ใส่ seed data เฉพาะเมื่อตารางว่าง
/// - DB_INIT=skip → ข้ามทั้งหมด (ใช้เมื่อ migrate จากที่อื่นแล้ว)
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, IConfiguration config, ILogger logger)
    {
        if (string.Equals(config["DB_INIT"], "skip", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("DB_INIT=skip — ข้ามการตรวจ migration และ seed data");
            return;
        }

        var sw = Stopwatch.StartNew();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("กำลัง apply migration {Count} รายการ: {Names}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
        }

        var seeded = await DbSeeder.SeedIfEmptyAsync(db);
        logger.LogInformation("เตรียมฐานข้อมูลเสร็จใน {Ms} ms (migration ใหม่ {Pending} รายการ, seed: {Seeded})",
            sw.ElapsedMilliseconds, pending.Count, seeded ? "ใส่แล้ว" : "ข้าม");
    }
}
