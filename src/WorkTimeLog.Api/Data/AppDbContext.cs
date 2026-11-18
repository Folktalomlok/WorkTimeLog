using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(p =>
        {
            p.ToTable("Projects");
            p.HasKey(x => x.Id);
            p.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100)
                .UseCollation("NOCASE"); // ชื่อซ้ำแบบไม่สนตัวพิมพ์เล็ก/ใหญ่
            p.HasIndex(x => x.Name).IsUnique();
            p.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever(); // DB default = 1 แต่ EF ส่งค่าเสมอ
        });

        modelBuilder.Entity<TimeEntry>(e =>
        {
            e.ToTable("TimeEntries", t =>
                t.HasCheckConstraint("CK_TimeEntries_Minutes", "\"Minutes\" > 0 AND \"Minutes\" <= 480"));
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkDate).IsRequired();
            e.Property(x => x.Description).IsRequired().HasMaxLength(500);
            e.Property(x => x.Minutes).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.IsCancelled).HasDefaultValue(false).ValueGeneratedNever();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasIndex(x => x.WorkDate);
            e.HasOne(x => x.Project)
                .WithMany(p => p.TimeEntries)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        Seed(modelBuilder);
    }

    private static void Seed(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>().HasData(
            new Project { Id = 1, Name = "โครงงานคอมพิวเตอร์", IsActive = true },
            new Project { Id = 2, Name = "ฝึกงาน / สหกิจศึกษา", IsActive = true },
            new Project { Id = 3, Name = "ทบทวนบทเรียนและการบ้าน", IsActive = true });

        var d1 = new DateOnly(2026, 11, 17);
        var d2 = new DateOnly(2026, 11, 18);
        var t1 = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<TimeEntry>().HasData(
            new TimeEntry { Id = 1, WorkDate = d1, ProjectId = 1, Description = "ออกแบบฐานข้อมูลและ ER Diagram", Minutes = 120, CreatedAt = t1, UpdatedAt = t1 },
            new TimeEntry { Id = 2, WorkDate = d1, ProjectId = 2, Description = "ประชุมทีมประจำสัปดาห์", Minutes = 60, Note = "ประชุมออนไลน์", CreatedAt = t1, UpdatedAt = t1 },
            new TimeEntry { Id = 3, WorkDate = d1, ProjectId = 3, Description = "ทำแบบฝึกหัดคณิตศาสตร์", Minutes = 90, Note = "ตัวอย่างรายการที่ถูกยกเลิก", IsCancelled = true, CreatedAt = t1, UpdatedAt = t1 },
            new TimeEntry { Id = 4, WorkDate = d2, ProjectId = 1, Description = "เขียน Web API ด้วย ASP.NET Core", Minutes = 180, CreatedAt = t2, UpdatedAt = t2 },
            new TimeEntry { Id = 5, WorkDate = d2, ProjectId = 2, Description = "จัดทำเอกสารรายงานประจำวัน", Minutes = 90, Note = "รออาจารย์ตรวจ", CreatedAt = t2, UpdatedAt = t2 });
    }
}
