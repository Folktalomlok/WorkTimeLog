using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Data;

/// <summary>
/// DbContext หลักที่ service ทุกตัวใช้ (ไม่ผูกกับ provider)
/// ตัวที่ใช้งานจริงคือ <see cref="SqliteAppDbContext"/> หรือ <see cref="PostgresAppDbContext"/>
/// ซึ่งแยกกันเพื่อให้แต่ละ provider มีชุด EF Migrations ของตัวเอง
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>collation แบบไม่สนตัวพิมพ์เล็ก/ใหญ่ของ PostgreSQL (ICU, nondeterministic)</summary>
    public const string PostgresCaseInsensitiveCollation = "case_insensitive";

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>สำหรับ class ลูกที่ส่ง DbContextOptions ของตัวเองเข้ามา</summary>
    protected AppDbContext(DbContextOptions options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isPostgres = Database.IsNpgsql();

        if (isPostgres)
        {
            // PostgreSQL ไม่มี NOCASE แบบ SQLite → สร้าง ICU collation ที่เทียบแบบไม่สนตัวพิมพ์
            // (ใช้กับ unique index และการเทียบ == ได้)
            modelBuilder.HasCollation(PostgresCaseInsensitiveCollation,
                locale: "und-u-ks-level2", provider: "icu", deterministic: false);
        }

        modelBuilder.Entity<Project>(p =>
        {
            p.ToTable("Projects");
            p.HasKey(x => x.Id);
            p.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100)
                // ชื่อซ้ำแบบไม่สนตัวพิมพ์เล็ก/ใหญ่
                .UseCollation(isPostgres ? PostgresCaseInsensitiveCollation : "NOCASE");
            p.HasIndex(x => x.Name).IsUnique();
            p.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever(); // DB default = true แต่ EF ส่งค่าเสมอ
        });

        modelBuilder.Entity<TimeEntry>(e =>
        {
            // ใส่ชื่อคอลัมน์ในเครื่องหมาย " ใช้ได้ทั้ง SQLite และ PostgreSQL
            e.ToTable("TimeEntries", t =>
                t.HasCheckConstraint("CK_TimeEntries_Minutes", "\"Minutes\" > 0 AND \"Minutes\" <= 480"));
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkDate).IsRequired();                 // SQLite: TEXT 'YYYY-MM-DD' / PostgreSQL: date
            e.Property(x => x.Description).IsRequired().HasMaxLength(500);
            e.Property(x => x.Minutes).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.IsCancelled).HasDefaultValue(false).ValueGeneratedNever();
            e.Property(x => x.CreatedAt).IsRequired();                // PostgreSQL: timestamptz (ต้องเป็น UTC)
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasIndex(x => x.WorkDate);
            e.HasOne(x => x.Project)
                .WithMany(p => p.TimeEntries)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // หมายเหตุ: seed data ไม่ได้อยู่ใน model แล้ว — ใส่ตอนเริ่มแอปผ่าน DbSeeder เฉพาะเมื่อตารางว่าง
    }
}

/// <summary>DbContext สำหรับ SQLite (migrations อยู่ใน Data/Migrations/Sqlite)</summary>
public class SqliteAppDbContext : AppDbContext
{
    public SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : base(options) { }
}

/// <summary>DbContext สำหรับ PostgreSQL / Neon (migrations อยู่ใน Data/Migrations/Postgres)</summary>
public class PostgresAppDbContext : AppDbContext
{
    public PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : base(options) { }
}
