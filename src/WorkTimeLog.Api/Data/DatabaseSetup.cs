using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkTimeLog.Api.Data;

public enum DbProvider { Sqlite, Postgres }

/// <summary>
/// ค่าฐานข้อมูลที่เลือกใช้ — <see cref="ConnectionString"/> อาจมีรหัสผ่าน ห้าม log / แสดงผล
/// ใช้ <see cref="Display"/> (ไม่มีรหัสผ่าน) แทนเมื่อต้องแสดง
/// </summary>
public sealed class DatabaseSettings
{
    public required DbProvider Provider { get; init; }
    public required string ConnectionString { get; init; }

    /// <summary>SQLite: path ไฟล์ / PostgreSQL: host และชื่อ database (ไม่มีรหัสผ่าน)</summary>
    public required string Display { get; init; }

    public string? Host { get; init; }
    public string? DatabaseName { get; init; }
    public string? FilePath { get; init; }

    public override string ToString() => $"{Provider}: {Display}"; // กันการ log connection string โดยไม่ตั้งใจ
}

public static class DatabaseSetup
{
    /// <summary>
    /// เลือก provider:
    /// - DB_PROVIDER=postgres | sqlite → ใช้ตามนั้น
    /// - ไม่ได้ตั้ง → postgres ถ้ามี ConnectionStrings:Default (จาก user-secrets / env ConnectionStrings__Default)
    ///                 ไม่มี → SQLite (DB_PATH หรือ worklog.db ในโฟลเดอร์โปรเจกต์)
    /// </summary>
    public static DatabaseSettings Resolve(IConfiguration config, string contentRoot)
    {
        var requested = config["DB_PROVIDER"]?.Trim().ToLowerInvariant();
        var pg = config.GetConnectionString("Default");

        var provider = requested switch
        {
            "postgres" or "postgresql" or "npgsql" => DbProvider.Postgres,
            "sqlite" => DbProvider.Sqlite,
            null or "" => string.IsNullOrWhiteSpace(pg) ? DbProvider.Sqlite : DbProvider.Postgres,
            _ => throw new InvalidOperationException($"ค่า DB_PROVIDER ไม่ถูกต้อง: \"{requested}\" (ใช้ได้: postgres, sqlite)"),
        };

        if (provider == DbProvider.Postgres)
        {
            if (string.IsNullOrWhiteSpace(pg))
                throw new InvalidOperationException(
                    "เลือก PostgreSQL แต่ไม่พบ ConnectionStrings:Default — ตั้งด้วย dotnet user-secrets หรือ environment variable ConnectionStrings__Default");

            NpgsqlConnectionStringBuilder b;
            try { b = new NpgsqlConnectionStringBuilder(pg); }
            catch (Exception) { throw new InvalidOperationException("ConnectionStrings:Default ไม่ใช่ connection string ของ PostgreSQL ที่ถูกต้อง"); }

            // Neon พัก compute เมื่อไม่มีการใช้งาน การต่อครั้งแรกอาจช้า → ขยาย timeout ถ้าไม่ได้กำหนดไว้เอง
            if (!pg.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                b.Timeout = 30;

            return new DatabaseSettings
            {
                Provider = DbProvider.Postgres,
                ConnectionString = b.ConnectionString,
                Host = b.Host,
                DatabaseName = b.Database,
                Display = $"{b.Host}/{b.Database}",
            };
        }

        var path = config["DB_PATH"];
        if (string.IsNullOrWhiteSpace(path)) path = config["Sqlite:Path"];
        if (string.IsNullOrWhiteSpace(path)) path = "worklog.db";
        path = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path)); // รวมตัวคั่น / และ \ ให้เป็นแบบเดียวกัน
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        return new DatabaseSettings
        {
            Provider = DbProvider.Sqlite,
            ConnectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString(),
            FilePath = path,
            Display = path,
        };
    }

    public static IServiceCollection AddAppDatabase(this IServiceCollection services, DatabaseSettings settings)
    {
        services.AddSingleton(settings);
        if (settings.Provider == DbProvider.Postgres)
            services.AddDbContext<AppDbContext, PostgresAppDbContext>(o => o.UseNpgsql(settings.ConnectionString));
        else
            services.AddDbContext<AppDbContext, SqliteAppDbContext>(o => o.UseSqlite(settings.ConnectionString));
        return services;
    }
}

// ---------- Design-time factories (ใช้ตอน dotnet ef migrations add / script เท่านั้น) ----------
// ใช้ connection string หลอกที่ไม่มีความลับ: การสร้าง migration / script ไม่ต้องต่อฐานข้อมูลจริง
// (อย่าใช้ dotnet ef database update — แอปรัน Migrate() ให้เองตอนเริ่มทำงาน)

public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=localhost;Database=design_time").Options);
}
