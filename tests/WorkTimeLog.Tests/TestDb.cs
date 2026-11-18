using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;

namespace WorkTimeLog.Tests;

/// <summary>
/// ฐานข้อมูล SQLite in-memory สำหรับแต่ละ test (สร้างใหม่ทุกครั้ง)
/// ใช้ SQLite จริงเพื่อให้พฤติกรรม SQL / constraint เหมือนตอนใช้งานจริง
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Context { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        Context = new AppDbContext(options);
        Context.Database.EnsureCreated(); // รวม seed data (Projects Id 1–3)
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
