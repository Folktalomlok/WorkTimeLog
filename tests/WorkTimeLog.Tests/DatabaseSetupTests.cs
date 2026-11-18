using Microsoft.Extensions.Configuration;
using WorkTimeLog.Api.Data;

namespace WorkTimeLog.Tests;

public class DatabaseSetupTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "wtl-tests");

    // รหัสผ่านสุ่มตอนรัน เพื่อตรวจว่าไม่หลุดไปใน Display / ToString (ไม่มีความลับจริงในไฟล์ test)
    private static readonly string FakeSecret = Guid.NewGuid().ToString("N");
    private static string FakePgConnection => $"Host=db.example.test;Database=testdb;Username=tester;PWD={FakeSecret}";

    [Fact(DisplayName = "26. ไม่มี ConnectionStrings:Default → ใช้ SQLite (worklog.db)")]
    public void NoConnectionString_UsesSqlite()
    {
        var s = DatabaseSetup.Resolve(Config(), TempRoot());

        Assert.Equal(DbProvider.Sqlite, s.Provider);
        Assert.Equal(Path.Combine(TempRoot(), "worklog.db"), s.FilePath);
    }

    [Fact(DisplayName = "27. มี ConnectionStrings:Default → ใช้ PostgreSQL และไม่แสดงรหัสผ่าน")]
    public void WithConnectionString_UsesPostgres_WithoutLeakingSecret()
    {
        var s = DatabaseSetup.Resolve(Config(("ConnectionStrings:Default", FakePgConnection)), TempRoot());

        Assert.Equal(DbProvider.Postgres, s.Provider);
        Assert.Equal("db.example.test", s.Host);
        Assert.Equal("testdb", s.DatabaseName);
        Assert.DoesNotContain(FakeSecret, s.Display);
        Assert.DoesNotContain(FakeSecret, s.ToString());
    }

    [Fact(DisplayName = "28. DB_PROVIDER=sqlite ใช้ SQLite แม้มี ConnectionStrings:Default")]
    public void ExplicitSqlite_OverridesConnectionString()
    {
        var s = DatabaseSetup.Resolve(Config(
            ("ConnectionStrings:Default", FakePgConnection), ("DB_PROVIDER", "sqlite"), ("DB_PATH", "data/x.db")), TempRoot());

        Assert.Equal(DbProvider.Sqlite, s.Provider);
        Assert.Equal(Path.Combine(TempRoot(), "data", "x.db"), s.FilePath);
    }

    [Fact(DisplayName = "29. DB_PROVIDER=postgres แต่ไม่มี connection string → error ชัดเจน")]
    public void ExplicitPostgres_WithoutConnectionString_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DatabaseSetup.Resolve(Config(("DB_PROVIDER", "postgres")), TempRoot()));
        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }

    [Fact(DisplayName = "30. Seed data ใส่เฉพาะตอนตารางว่าง (รันซ้ำไม่ซ้ำ)")]
    public async Task Seeder_OnlyWhenEmpty()
    {
        using var db = new TestDb(); // TestDb seed ไปแล้ว 1 ครั้ง
        Assert.False(await DbSeeder.SeedIfEmptyAsync(db.Context));
        Assert.Equal(3, db.Context.Projects.Count());
        Assert.Equal(5, db.Context.TimeEntries.Count());
    }
}
