using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Admin;

/// <summary>
/// รหัสผ่านแอดมิน — อ่านจาก ADMIN_PASSWORD (env / Vercel / user-secrets) เท่านั้น ไม่มีค่าเริ่มต้น
/// ห้าม log ค่า
/// </summary>
public sealed class AdminPassword
{
    private readonly byte[]? _hash;

    public AdminPassword(IConfiguration config)
    {
        var pw = config["ADMIN_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(pw))
        {
            _hash = Sha256(pw);
            // ใส่ไว้ใน cookie เพื่อให้ cookie เดิมใช้ไม่ได้ทันทีเมื่อเปลี่ยนรหัส (เป็น hash ที่ผ่าน HMAC อีกชั้น ไม่ใช่รหัส)
            Fingerprint = Convert.ToHexString(HMACSHA256.HashData(_hash, "wtl-admin-session"u8.ToArray()))[..16];
        }
    }

    /// <summary>ตั้ง ADMIN_PASSWORD แล้วหรือยัง — ถ้ายัง หน้าแอดมินถูกปิด (503)</summary>
    public bool IsConfigured => _hash is not null;

    public string? Fingerprint { get; }

    /// <summary>
    /// เทียบรหัสแบบ constant-time: hash ทั้งสองฝั่งด้วย SHA-256 ให้ยาวเท่ากันก่อน
    /// แล้วใช้ CryptographicOperations.FixedTimeEquals (เวลาไม่ขึ้นกับว่าตัวอักษรตรงกันกี่ตัว)
    /// </summary>
    public bool Verify(string? candidate)
    {
        if (_hash is null || candidate is null) return false;
        return CryptographicOperations.FixedTimeEquals(_hash, Sha256(candidate));
    }

    private static byte[] Sha256(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));
}

public record AdminLoginRequest(string? Password);

public static class AdminAuth
{
    public const string PolicyName = "Admin";
    public const string CookieName = "wtl_admin";
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private const string FingerprintClaim = "wtl:pwfp";

    /// <summary>
    /// Cookie Authentication + Data Protection key ring ใน PostgreSQL/SQLite
    /// (เหตุผล: cookie ถูกเข้ารหัสด้วย key ring — ถ้าแต่ละ instance สร้าง key เองในหน่วยความจำ
    /// cookie ที่ออกจาก instance หนึ่งจะใช้กับอีก instance หรือหลัง scale-to-zero ไม่ได้ ผู้ใช้จะหลุดล็อกอินเอง)
    /// </summary>
    public static IServiceCollection AddAdminAuth(this IServiceCollection services, bool withDatabase)
    {
        services.AddSingleton<AdminPassword>();

        var dp = services.AddDataProtection().SetApplicationName("WorkTimeLog");
        if (withDatabase)
            dp.PersistKeysToDbContext<AppDbContext>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = SessionLifetime;
                o.SlidingExpiration = false;
                o.LoginPath = "/admin/login";
                o.LogoutPath = "/api/admin/logout";
                o.Events = new CookieAuthenticationEvents
                {
                    // API → 401/403 เป็น JSON, หน้าเว็บ → ไปหน้า login
                    OnRedirectToLogin = ctx => IsApi(ctx.Request)
                        ? WriteJson(ctx.Response, 401, "กรุณาเข้าสู่ระบบแอดมินก่อน")
                        : Redirect(ctx.Response, "/admin/login"),
                    OnRedirectToAccessDenied = ctx => IsApi(ctx.Request)
                        ? WriteJson(ctx.Response, 403, "ไม่มีสิทธิ์เข้าถึง")
                        : Redirect(ctx.Response, "/admin/login"),
                    // เปลี่ยน ADMIN_PASSWORD แล้ว cookie เก่าใช้ไม่ได้
                    OnValidatePrincipal = async ctx =>
                    {
                        var pw = ctx.HttpContext.RequestServices.GetRequiredService<AdminPassword>();
                        var fp = ctx.Principal?.FindFirstValue(FingerprintClaim);
                        if (!pw.IsConfigured || fp != pw.Fingerprint)
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    },
                };
            });

        services.AddAuthorization(o =>
            o.AddPolicy(PolicyName, p => p.RequireAuthenticatedUser().RequireRole("admin")));
        return services;
    }

    /// <summary>ถ้าไม่ได้ตั้ง ADMIN_PASSWORD → ปิดทุกอย่างของแอดมิน (503) ห้ามเปิดให้เข้าโดยไม่มีรหัส</summary>
    public static IApplicationBuilder UseAdminGate(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (IsAdminArea(ctx.Request.Path) && !ctx.RequestServices.GetRequiredService<AdminPassword>().IsConfigured)
            {
                if (IsApi(ctx.Request))
                {
                    await WriteJson(ctx.Response, 503, "หน้าแอดมินถูกปิด: ยังไม่ได้ตั้งค่า ADMIN_PASSWORD");
                }
                else
                {
                    ctx.Response.StatusCode = 503;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.WriteAsync(
                        "<!doctype html><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\">" +
                        "<title>ปิดใช้งาน</title><body style=\"font-family:sans-serif;padding:2rem\">" +
                        "<h1>หน้าแอดมินถูกปิด</h1><p>ผู้ดูแลระบบยังไม่ได้ตั้งค่า ADMIN_PASSWORD</p><p><a href=\"/\">กลับหน้าหลัก</a></p>");
                }
                return;
            }
            await next();
        });

    public static WebApplication MapAdmin(this WebApplication app)
    {
        var uiRoot = ResolveUiRoot(app.Environment);

        // ---- หน้าเว็บ ----
        app.MapGet("/admin", () => Page(uiRoot, "index.html")).RequireAuthorization(PolicyName);
        app.MapGet("/admin/assets/admin.js", () => Asset(uiRoot, "admin.js")).RequireAuthorization(PolicyName);
        app.MapGet("/admin/login", (HttpContext ctx) =>
            ctx.User.IsInRole("admin") ? Results.Redirect("/admin") : Page(uiRoot, "login.html")).AllowAnonymous();
        app.MapGet("/admin/assets/login.js", () => Asset(uiRoot, "login.js")).AllowAnonymous();

        // ---- API ----
        app.MapPost("/api/admin/login", LoginAsync).AllowAnonymous();
        app.MapPost("/api/admin/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();
        app.MapGet("/api/admin/me", (HttpContext ctx) =>
            Results.Json(new { authenticated = ctx.User.IsInRole("admin") })).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext ctx, AdminLoginRequest? body, AdminPassword password, AppDbContext db, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("AdminLogin");
        var ip = ClientIp(ctx);
        var now = DateTime.UtcNow;
        var since = now - FailureWindow;

        // ลบบันทึกเก่าเกิน 1 วัน (ตารางไม่โตไม่สิ้นสุด)
        await db.AdminLoginAttempts.Where(a => a.AttemptedAt < now.AddDays(-1)).ExecuteDeleteAsync();

        var failures = await db.AdminLoginAttempts
            .Where(a => a.IpAddress == ip && a.AttemptedAt >= since)
            .OrderBy(a => a.AttemptedAt)
            .Select(a => a.AttemptedAt)
            .ToListAsync();

        if (failures.Count >= MaxFailedAttempts)
        {
            var retryAt = failures[failures.Count - MaxFailedAttempts] + FailureWindow;
            var wait = Math.Max(1, (int)Math.Ceiling((retryAt - now).TotalMinutes));
            ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling((retryAt - now).TotalSeconds)).ToString();
            log.LogWarning("Admin login blocked (rate limit) for {Ip}", ip);
            return Results.Json(new ErrorResponse($"ลองรหัสผิดครบ {MaxFailedAttempts} ครั้งแล้ว กรุณารออีกประมาณ {wait} นาที"), statusCode: 429);
        }

        if (!password.Verify(body?.Password))
        {
            db.AdminLoginAttempts.Add(new AdminLoginAttempt { IpAddress = ip, AttemptedAt = now });
            await db.SaveChangesAsync();
            var left = MaxFailedAttempts - failures.Count - 1;
            log.LogWarning("Admin login failed for {Ip} ({Left} attempts left)", ip, left);
            return Results.Json(new ErrorResponse(left > 0
                ? $"รหัสผ่านไม่ถูกต้อง (เหลือโอกาสอีก {left} ครั้ง)"
                : $"รหัสผ่านไม่ถูกต้อง ลองครบ {MaxFailedAttempts} ครั้งแล้ว กรุณารอ {FailureWindow.TotalMinutes:0} นาที"), statusCode: 401);
        }

        // สำเร็จ → ล้างประวัติรหัสผิดของ IP นี้
        await db.AdminLoginAttempts.Where(a => a.IpAddress == ip).ExecuteDeleteAsync();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(FingerprintClaim, password.Fingerprint!),
        }, CookieAuthenticationDefaults.AuthenticationScheme);

        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.Add(SessionLifetime), AllowRefresh = false });
        log.LogInformation("Admin login succeeded from {Ip}", ip);
        return Results.Json(new { ok = true, expiresInHours = SessionLifetime.TotalHours });
    }

    // ---------------- helpers ----------------

    private static bool IsAdminArea(PathString p) =>
        p.StartsWithSegments("/admin") || p.StartsWithSegments("/api/admin") || p.StartsWithSegments("/api/database");

    private static bool IsApi(HttpRequest r) => r.Path.StartsWithSegments("/api");

    private static string ClientIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() is { Length: > 0 } ip ? ip[..Math.Min(ip.Length, 64)] : "unknown";

    private static Task WriteJson(HttpResponse res, int status, string message)
    {
        res.StatusCode = status;
        res.Headers.CacheControl = "no-store";
        return res.WriteAsJsonAsync(new ErrorResponse(message));
    }

    private static Task Redirect(HttpResponse res, string to)
    {
        res.Redirect(to);
        return Task.CompletedTask;
    }

    private static string ResolveUiRoot(IWebHostEnvironment env)
    {
        foreach (var root in new[] { env.ContentRootPath, AppContext.BaseDirectory })
        {
            var p = Path.Combine(root, "AdminUi");
            if (Directory.Exists(p)) return p;
        }
        return Path.Combine(AppContext.BaseDirectory, "AdminUi");
    }

    private static IResult Page(string root, string file) => new NoStoreFileResult(Path.Combine(root, file), "text/html; charset=utf-8");
    private static IResult Asset(string root, string file) => new NoStoreFileResult(Path.Combine(root, file), "text/javascript; charset=utf-8");

    /// <summary>ส่งไฟล์พร้อม Cache-Control: no-store (หน้าแอดมินไม่ควรถูก cache ที่ CDN/เบราว์เซอร์)</summary>
    private sealed class NoStoreFileResult(string path, string contentType) : IResult
    {
        public Task ExecuteAsync(HttpContext ctx)
        {
            if (!File.Exists(path))
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            }
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.File(path, contentType).ExecuteAsync(ctx);
        }
    }
}
