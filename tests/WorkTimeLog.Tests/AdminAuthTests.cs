using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorkTimeLog.Tests;

/// <summary>
/// รันแอปจริงในหน่วยความจำ (TestServer) ด้วย:
/// - environment "Testing" → ไม่โหลด user-secrets (ไม่แตะ Neon / รหัสจริงของเครื่อง)
/// - SQLite ไฟล์ชั่วคราวของแต่ละ test
/// - ADMIN_PASSWORD สุ่มตอนรัน (ไม่มีรหัสจริงในไฟล์ test)
/// </summary>
public sealed class AdminAppFactory : WebApplicationFactory<Program>
{
    public string? Password { get; }
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"wtl-admin-test-{Guid.NewGuid():N}.db");

    public AdminAppFactory(bool withPassword = true) =>
        Password = withPassword ? "pw-" + Guid.NewGuid().ToString("N") : null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_PROVIDER", "sqlite");
        builder.UseSetting("DB_PATH", DbPath);
        builder.UseSetting("ConnectionStrings:Default", "");
        builder.UseSetting("ADMIN_PASSWORD", Password ?? "");
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), // cookie เป็น Secure → ต้องเป็น https
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { DbPath, DbPath + "-wal", DbPath + "-shm" })
            try { File.Delete(f); } catch { /* ignore */ }
    }
}

public class AdminAuthTests
{
    private static Task<HttpResponseMessage> Login(HttpClient c, string? password) =>
        c.PostAsJsonAsync("/api/admin/login", new { password });

    private static async Task<string> Message(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    [Fact(DisplayName = "31. ไม่ล็อกอิน → /api/database/* ได้ 401")]
    public async Task NotLoggedIn_DatabaseApi_Returns401()
    {
        using var app = new AdminAppFactory();
        var c = app.NewClient();

        foreach (var path in new[] { "/api/database/info", "/api/database/entries", "/api/database/projects",
                     "/api/database/daily-totals", "/api/database/export/entries.csv", "/api/database/download" })
        {
            var r = await c.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        Assert.Contains("เข้าสู่ระบบ", await Message(await c.GetAsync("/api/database/info")));
    }

    [Fact(DisplayName = "32. รหัสผิด → 401 และยังเข้า /api/database ไม่ได้")]
    public async Task WrongPassword_IsRejected()
    {
        using var app = new AdminAppFactory();
        var c = app.NewClient();

        var r = await Login(c, "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("รหัสผ่านไม่ถูกต้อง", await Message(r));
        Assert.False(r.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/database/info")).StatusCode);
    }

    [Fact(DisplayName = "33. รหัสถูก → เข้าได้ และ cookie เป็น HttpOnly, Secure, SameSite=Strict, อายุ 8 ชม.")]
    public async Task CorrectPassword_GrantsAccess_WithSecureCookie()
    {
        using var app = new AdminAppFactory();
        var c = app.NewClient();

        var r = await Login(c, app.Password);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cookie = Assert.Single(r.Headers.GetValues("Set-Cookie"), v => v.StartsWith("wtl_admin="));
        var lower = cookie.ToLowerInvariant();
        Assert.Contains("httponly", lower);
        Assert.Contains("secure", lower);
        Assert.Contains("samesite=strict", lower);
        var expires = DateTimeOffset.Parse(
            cookie.Split(';').Single(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Split('=', 2)[1],
            System.Globalization.CultureInfo.InvariantCulture); // culture th-TH ใช้ปฏิทินพุทธ อ่านวันที่ HTTP ผิด
        Assert.InRange((expires - DateTimeOffset.UtcNow).TotalHours, 7.9, 8.1);

        var info = await c.GetAsync("/api/database/info");
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        var json = JsonDocument.Parse(await info.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("SQLite", json.GetProperty("databaseType").GetString());
        Assert.Equal(app.DbPath, json.GetProperty("filePath").GetString()); // ยืนยันว่าใช้ฐานข้อมูลชั่วคราว ไม่ใช่ของจริง

        var page = await c.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("ออกจากระบบ", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/assets/admin.js")).StatusCode);
    }

    [Fact(DisplayName = "34. ไม่ตั้ง ADMIN_PASSWORD → หน้าแอดมินและ API ตอบ 503")]
    public async Task NoAdminPassword_Returns503()
    {
        using var app = new AdminAppFactory(withPassword: false);
        var c = app.NewClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/admin/login")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/api/database/info")).StatusCode);
        var login = await Login(c, "");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
        Assert.Contains("ADMIN_PASSWORD", await Message(login));

        // หน้าหลักยังใช้ได้
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/projects")).StatusCode);
    }

    [Fact(DisplayName = "35. ลองรหัสผิด 5 ครั้ง → ครั้งที่ 6 ได้ 429 แม้รหัสถูก")]
    public async Task FiveFailures_BlocksFurtherAttempts()
    {
        using var app = new AdminAppFactory();
        var c = app.NewClient();

        for (var i = 1; i <= 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, $"wrong-{i}")).StatusCode);

        var blocked = await Login(c, app.Password);
        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Contains("กรุณารอ", await Message(blocked));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/database/info")).StatusCode);
    }

    [Fact(DisplayName = "36. ออกจากระบบ → กลับมาได้ 401")]
    public async Task Logout_RevokesAccess()
    {
        using var app = new AdminAppFactory();
        var c = app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await Login(c, app.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/database/info")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/admin/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/database/info")).StatusCode);
    }

    [Fact(DisplayName = "37. หน้าแอดมิน: ไม่ล็อกอิน → redirect ไป /admin/login, หน้า login เปิดได้")]
    public async Task AdminPage_RedirectsToLogin()
    {
        using var app = new AdminAppFactory();
        var c = app.NewClient();

        var r = await c.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/admin/login", r.Headers.Location!.OriginalString);
        // ไฟล์ JS ของหน้าแอดมินก็ต้องล็อกอินก่อน (ไม่ใช่ API จึง redirect ไปหน้า login)
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/admin/assets/admin.js")).StatusCode);

        var login = await c.GetAsync("/admin/login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("เข้าสู่ระบบแอดมิน", await login.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "38. หน้าหลักไม่มีปุ่มดูฐานข้อมูล, /database.html เดิมไม่มีแล้ว, API ผู้ใช้ทั่วไปยังใช้ได้")]
    public async Task HomePage_HasNoDatabaseLink()
    {
        using var app = new AdminAppFactory();
        var c = app.NewClient();

        var home = await (await c.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("บันทึกเวลาปฏิบัติงาน", home);
        Assert.DoesNotContain("database.html", home);
        Assert.DoesNotContain("ดูฐานข้อมูล", home);
        Assert.DoesNotContain("/admin", home);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/database.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/js/database.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/summary/daily?date=2026-11-18")).StatusCode);
    }
}
