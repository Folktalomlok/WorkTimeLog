using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;

namespace WorkTimeLog.Api.Services;

/// <summary>
/// ดูข้อมูลในฐานข้อมูลแบบอ่านอย่างเดียว (ใช้กับหน้า database.html)
/// ไม่มีเมธอดที่แก้ไขหรือลบข้อมูล
/// </summary>
public class DatabaseService
{
    private readonly AppDbContext _db;

    public DatabaseService(AppDbContext db) => _db = db;

    /// <summary>path ของไฟล์ฐานข้อมูล (":memory:" ถ้าเป็นฐานข้อมูลในหน่วยความจำ)</summary>
    public string DataSource =>
        new SqliteConnectionStringBuilder(_db.Database.GetConnectionString()).DataSource;

    public async Task<DatabaseInfoDto> GetInfoAsync()
    {
        var conn = (SqliteConnection)_db.Database.GetDbConnection();
        var opened = conn.State != ConnectionState.Open;
        if (opened) await conn.OpenAsync();
        string? version;
        var tables = new List<TableCountDto>();
        try
        {
            version = await ScalarAsync<string>(conn, "SELECT sqlite_version();");

            var names = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) names.Add(r.GetString(0));
            }
            foreach (var name in names)
            {
                // ชื่อตารางมาจาก sqlite_master เอง และ escape เครื่องหมาย " ไว้แล้ว
                var rows = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\";");
                tables.Add(new TableCountDto(name, rows));
            }
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }

        var entryCount = await _db.TimeEntries.CountAsync();
        var cancelled = await _db.TimeEntries.CountAsync(e => e.IsCancelled);
        var minutes = await _db.TimeEntries.Where(e => !e.IsCancelled).SumAsync(e => e.Minutes);

        var path = DataSource;
        return new DatabaseInfoDto(
            "SQLite",
            version,
            path,
            FileSize(path),
            FileSize(path + "-wal"),
            tables,
            await _db.Projects.CountAsync(),
            entryCount,
            entryCount - cancelled,
            cancelled,
            minutes,
            DateTime.UtcNow);
    }

    public async Task<List<DbProjectRowDto>> GetProjectsAsync()
    {
        return await _db.Projects
            .OrderBy(p => p.Id)
            .Select(p => new DbProjectRowDto(
                p.Id,
                p.Name,
                p.IsActive,
                p.TimeEntries.Count(),
                p.TimeEntries.Where(e => !e.IsCancelled).Sum(e => (int?)e.Minutes) ?? 0))
            .ToListAsync();
    }

    /// <summary>ทุกแถวของ TimeEntries (รวมรายการที่ยกเลิก) เรียง WorkDate ล่าสุดก่อน</summary>
    public async Task<List<TimeEntryDto>> GetEntriesAsync()
    {
        var list = await _db.TimeEntries.AsNoTracking()
            .Include(e => e.Project)
            .OrderByDescending(e => e.WorkDate)
            .ThenByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .ToListAsync();
        return list.Select(e => e.ToDto()).ToList();
    }

    /// <summary>ผลรวมนาทีต่อวัน ไม่นับรายการที่ยกเลิก เรียงวันล่าสุดก่อน</summary>
    public async Task<List<DailyTotalDto>> GetDailyTotalsAsync()
    {
        var rows = await _db.TimeEntries
            .GroupBy(e => e.WorkDate)
            .Select(g => new
            {
                Date = g.Key,
                Total = g.Where(e => !e.IsCancelled).Sum(e => (int?)e.Minutes) ?? 0,
                Active = g.Count(e => !e.IsCancelled),
                Cancelled = g.Count(e => e.IsCancelled),
            })
            .ToListAsync();

        const int max = TimeEntryService.MaxDailyMinutes;
        return rows
            .OrderByDescending(r => r.Date)
            .Select(r => new DailyTotalDto(r.Date, r.Total, Math.Max(0, max - r.Total), r.Active, r.Cancelled, r.Total > max))
            .ToList();
    }

    /// <summary>TimeEntries เป็น CSV แบบ UTF-8 with BOM (Excel แสดงภาษาไทยได้)</summary>
    public async Task<byte[]> ExportEntriesCsvAsync()
    {
        var entries = await GetEntriesAsync();
        var sb = new StringBuilder();
        sb.Append("Id,WorkDate,ProjectId,ProjectName,Description,Minutes,Note,IsCancelled,CreatedAt,UpdatedAt\r\n");
        foreach (var e in entries)
        {
            sb.Append(e.Id).Append(',')
              .Append(TimeEntryService.FormatDate(e.WorkDate)).Append(',')
              .Append(e.ProjectId).Append(',')
              .Append(CsvText(e.ProjectName)).Append(',')
              .Append(CsvText(e.Description)).Append(',')
              .Append(e.Minutes).Append(',')
              .Append(CsvText(e.Note)).Append(',')
              .Append(e.IsCancelled ? "true" : "false").Append(',')
              .Append(e.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append(e.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("\r\n");
        }
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. bom, .. body];
    }

    /// <summary>
    /// สร้างสำเนาไฟล์ฐานข้อมูลเป็นไฟล์ชั่วคราว:
    /// 1) PRAGMA wal_checkpoint(FULL) ให้ข้อมูลล่าสุดจาก WAL เข้าไฟล์หลัก
    /// 2) คัดลอกด้วย SQLite Online Backup API (ได้สำเนาที่สมบูรณ์แม้มีการเขียนพร้อมกัน)
    /// ผู้เรียกต้องลบไฟล์เมื่อใช้เสร็จ
    /// </summary>
    public async Task<string> CreateDatabaseCopyAsync()
    {
        var conn = (SqliteConnection)_db.Database.GetDbConnection();
        var opened = conn.State != ConnectionState.Open;
        if (opened) await conn.OpenAsync();
        var tempPath = Path.Combine(Path.GetTempPath(), $"worklog-{Guid.NewGuid():N}.db");
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
                await cmd.ExecuteNonQueryAsync();
            }

            await using var dest = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = tempPath, Pooling = false }.ToString());
            await dest.OpenAsync();
            conn.BackupDatabase(dest);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
        return tempPath;
    }

    // ---------------- helpers ----------------

    private static async Task<T?> ScalarAsync<T>(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture);
    }

    private static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    /// <summary>escape ข้อความสำหรับ CSV และกัน formula injection ใน Excel (=, +, -, @)</summary>
    internal static string CsvText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var v = value;
        if (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 || v != value
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }
}
