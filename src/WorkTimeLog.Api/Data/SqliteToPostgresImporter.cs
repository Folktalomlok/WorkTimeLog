using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Data;

/// <summary>
/// ย้ายข้อมูลจากไฟล์ SQLite เข้า PostgreSQL โดยคง Id เดิม
/// - รันซ้ำได้: แถวที่มี Id อยู่แล้วใน PostgreSQL จะถูกข้าม (ไม่ทับ ไม่ซ้ำ)
/// - รีเซ็ต identity sequence ให้ต่อจาก Id มากสุด
/// - เปิดไฟล์ SQLite แบบอ่านอย่างเดียว
/// วิธีรัน: dotnet run --project src/WorkTimeLog.Api -- import-sqlite [path ไฟล์ .db]
/// </summary>
public static class SqliteToPostgresImporter
{
    public static async Task<int> RunAsync(AppDbContext target, string sourcePath, TextWriter output)
    {
        if (!target.Database.IsNpgsql())
        {
            output.WriteLine("ปลายทางไม่ใช่ PostgreSQL — ตั้ง ConnectionStrings:Default (หรือ DB_PROVIDER=postgres) ก่อน");
            return 2;
        }
        if (!File.Exists(sourcePath))
        {
            output.WriteLine($"ไม่พบไฟล์ SQLite: {sourcePath}");
            return 2;
        }

        output.WriteLine("รัน migration กับ PostgreSQL ...");
        await target.Database.MigrateAsync();

        var srcCs = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var source = new SqliteAppDbContext(
            new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(srcCs).Options);

        var srcProjects = await source.Projects.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        var srcEntries = await source.TimeEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();

        var before = await CountAsync(target);
        output.WriteLine();
        var srcMinutesBefore = srcEntries.Where(e => !e.IsCancelled).Sum(e => e.Minutes);
        var tgtMinutesBefore = await target.TimeEntries.Where(e => !e.IsCancelled).SumAsync(e => e.Minutes);
        output.WriteLine($"ต้นทาง  SQLite     : Projects = {srcProjects.Count}, TimeEntries = {srcEntries.Count}, เวลารวม = {srcMinutesBefore} นาที");
        output.WriteLine($"ก่อนย้าย PostgreSQL : Projects = {before.Projects}, TimeEntries = {before.Entries}, เวลารวม = {tgtMinutesBefore} นาที");

        var existingProjects = await target.Projects.AsNoTracking().ToListAsync();
        var existingProjectIds = existingProjects.Select(p => p.Id).ToHashSet();
        var existingEntryIds = (await target.TimeEntries.AsNoTracking().Select(e => e.Id).ToListAsync()).ToHashSet();

        // ชื่อโครงการซ้ำแต่ Id ต่างกัน → ย้ายไม่ได้ (unique index) ให้หยุดก่อนแก้อะไร
        var conflicts = srcProjects
            .Where(s => !existingProjectIds.Contains(s.Id))
            .Where(s => existingProjects.Any(t => string.Equals(t.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (conflicts.Count > 0)
        {
            output.WriteLine("หยุด: มีชื่อโครงการซ้ำกับข้อมูลใน PostgreSQL แต่ Id ต่างกัน:");
            foreach (var c in conflicts) output.WriteLine($"  - Id {c.Id}: {c.Name}");
            return 3;
        }

        var newProjects = srcProjects.Where(p => !existingProjectIds.Contains(p.Id))
            .Select(p => new Project { Id = p.Id, Name = p.Name, IsActive = p.IsActive })
            .ToList();
        var newEntries = srcEntries.Where(e => !existingEntryIds.Contains(e.Id))
            .Select(e => new TimeEntry
            {
                Id = e.Id,
                WorkDate = e.WorkDate,
                ProjectId = e.ProjectId,
                Description = e.Description,
                Minutes = e.Minutes,
                Note = e.Note,
                IsCancelled = e.IsCancelled,
                // SQLite เก็บเป็นข้อความไม่มี timezone (แอปบันทึกเป็น UTC) — timestamptz ต้องการ Kind = Utc
                CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
                UpdatedAt = DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc),
            })
            .ToList();

        await using (var tx = await target.Database.BeginTransactionAsync())
        {
            target.Projects.AddRange(newProjects);
            await target.SaveChangesAsync();
            target.TimeEntries.AddRange(newEntries);
            await target.SaveChangesAsync();

            // ให้ Id ถัดไปต่อจาก Id มากสุด (เพราะใส่ Id เองไป sequence จึงยังไม่ขยับ)
            await target.Database.ExecuteSqlRawAsync(
                "SELECT setval(pg_get_serial_sequence('\"Projects\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"Projects\"), 0) + 1, false);");
            await target.Database.ExecuteSqlRawAsync(
                "SELECT setval(pg_get_serial_sequence('\"TimeEntries\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"TimeEntries\"), 0) + 1, false);");

            await tx.CommitAsync();
        }
        target.ChangeTracker.Clear();

        var after = await CountAsync(target);
        output.WriteLine($"เพิ่มใหม่           : Projects = {newProjects.Count}, TimeEntries = {newEntries.Count}");
        output.WriteLine($"ข้าม (มี Id อยู่แล้ว) : Projects = {srcProjects.Count - newProjects.Count}, TimeEntries = {srcEntries.Count - newEntries.Count}");
        output.WriteLine($"หลังย้าย PostgreSQL : Projects = {after.Projects}, TimeEntries = {after.Entries}");

        // ตรวจว่าทุก Id ของต้นทางมีอยู่ในปลายทาง และเวลารวม (ไม่นับที่ยกเลิก) ตรงกัน
        var tgtProjectIds = (await target.Projects.Select(p => p.Id).ToListAsync()).ToHashSet();
        var tgtEntryIds = (await target.TimeEntries.Select(e => e.Id).ToListAsync()).ToHashSet();
        var missing = srcProjects.Count(p => !tgtProjectIds.Contains(p.Id)) + srcEntries.Count(e => !tgtEntryIds.Contains(e.Id));
        var srcMinutes = srcEntries.Where(e => !e.IsCancelled).Sum(e => e.Minutes);
        // เทียบเวลารวมเฉพาะแถวที่มาจากต้นทาง (Id ชุดเดียวกัน)
        var srcIds = srcEntries.Select(e => e.Id).ToList();
        var tgtMinutes = await target.TimeEntries.Where(e => !e.IsCancelled && srcIds.Contains(e.Id))
            .SumAsync(e => e.Minutes);
        var tgtAllMinutes = await target.TimeEntries.Where(e => !e.IsCancelled).SumAsync(e => e.Minutes);

        var countsMatch = after.Projects == srcProjects.Count && after.Entries == srcEntries.Count;
        output.WriteLine($"เวลารวม (ไม่นับยกเลิก): SQLite = {srcMinutes} นาที, PostgreSQL = {tgtMinutes} นาที (ทั้งตาราง {tgtAllMinutes} นาที)");
        output.WriteLine(missing == 0 && countsMatch && srcMinutes == tgtMinutes
            ? "ผลตรวจ: ตรงกันทั้งหมด ✔"
            : missing == 0
                ? "ผลตรวจ: ทุกแถวของต้นทางย้ายครบ แต่ PostgreSQL มีแถวอื่นเพิ่มเติม (เช่นข้อมูลที่เพิ่มหลังย้าย)"
                : $"ผลตรวจ: ขาดไป {missing} แถว ✘");
        return missing == 0 ? 0 : 1;
    }

    private static async Task<(int Projects, int Entries)> CountAsync(AppDbContext db) =>
        (await db.Projects.CountAsync(), await db.TimeEntries.CountAsync());
}
