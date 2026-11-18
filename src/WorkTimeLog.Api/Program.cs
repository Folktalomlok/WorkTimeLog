using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using WorkTimeLog.Api.Admin;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Filters;
using WorkTimeLog.Api.Services;

var startupClock = Stopwatch.StartNew();
Console.OutputEncoding = Encoding.UTF8;

// โหมดย้ายข้อมูล: dotnet run --project src/WorkTimeLog.Api -- import-sqlite [path ไฟล์ .db]
var importMode = args.Length > 0 && args[0] == "import-sqlite";
var builder = WebApplication.CreateBuilder(importMode ? Array.Empty<string>() : args);

// ---------- Kestrel: ฟังที่ http://0.0.0.0:{PORT} ----------
// Vercel / Render กำหนดพอร์ตผ่าน PORT (Dockerfile.vercel ตั้ง PORT=80) ถ้าไม่มี ใช้ค่า Port ใน appsettings (5000)
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var envPort)
    ? envPort
    : builder.Configuration.GetValue("Port", 5000);
builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Any, port));
// ไม่ใช้ UseHttpsRedirection: Vercel / Render / Cloudflare ทำ HTTPS ที่ด้านหน้า แล้วส่งต่อมาเป็น HTTP
// (ถ้าเปิด จะ redirect วนหรือพังหลัง proxy)

// ---------- รับ X-Forwarded-For / X-Forwarded-Proto จาก proxy ----------
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // IP ของ proxy บน Vercel/Render ไม่คงที่ จึงเชื่อ header จาก proxy ทุกตัว (แอปเข้าถึงได้ผ่าน proxy เท่านั้น)
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---------- Database: PostgreSQL (Neon) หรือ SQLite ----------
// ดู DatabaseSetup.Resolve: DB_PROVIDER / ConnectionStrings:Default (user-secrets หรือ env ConnectionStrings__Default) / DB_PATH
DatabaseSettings? dbSettings = null;
string? dbConfigError = null;
try
{
    dbSettings = DatabaseSetup.Resolve(builder.Configuration, builder.Environment.ContentRootPath);
    builder.Services.AddAppDatabase(dbSettings);

    builder.Services.AddScoped<TimeEntryService>();
    builder.Services.AddScoped<SummaryService>();
    builder.Services.AddScoped<ProjectService>();
    builder.Services.AddScoped<DatabaseService>();

    // หน้าแอดมิน: Cookie Authentication + Data Protection key ในฐานข้อมูล (ดู Admin/AdminAuth.cs)
    builder.Services.AddAdminAuth(withDatabase: true);
}
catch (InvalidOperationException ex)
{
    // เช่น DB_PROVIDER=postgres แต่ไม่มี ConnectionStrings__Default → ไม่ fallback ไป SQLite
    dbConfigError = ex.Message;
}

builder.Services
    .AddControllers(o => o.Filters.Add<ApiExceptionFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping) // แสดงภาษาไทยตรง ๆ ไม่เป็น \uXXXX
    .ConfigureApiBehaviorOptions(o =>
    {
        // ข้อมูลผิดรูปแบบ (เช่น วันที่ไม่ถูกต้อง, JSON เสีย) → 400 พร้อมข้อความภาษาไทย
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var fields = ctx.ModelState
                .Where(kv => kv.Value is { Errors.Count: > 0 } && !string.IsNullOrEmpty(kv.Key))
                .Select(kv => kv.Key.TrimStart('$', '.'))
                .Where(k => k.Length > 0 && k != "dto")
                .Distinct()
                .ToList();
            var msg = fields.Count > 0
                ? $"ข้อมูลที่ส่งมาไม่ถูกต้อง กรุณาตรวจสอบ: {string.Join(", ", fields)}"
                : "ข้อมูลที่ส่งมาไม่ถูกต้อง";
            return new BadRequestObjectResult(new ErrorResponse(msg));
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Work Time Log API", Version = "v1", Description = "ระบบบันทึกเวลาปฏิบัติงาน" });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
});

// ---------- CORS (สำหรับการพัฒนา) ----------
builder.Services.AddCors(o => o.AddPolicy("DevCors", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseForwardedHeaders();

// ---------- ตั้งค่าฐานข้อมูลไม่ครบ: เปิดแอปได้ แต่ตอบ 503 พร้อมข้อความชัดเจน ----------
if (dbConfigError is not null)
{
    if (importMode)
    {
        Console.Error.WriteLine($"ย้ายข้อมูลไม่ได้: {dbConfigError}");
        Environment.ExitCode = 2;
        return;
    }

    app.Logger.LogCritical("ตั้งค่าฐานข้อมูลไม่ครบ: {Error}", dbConfigError);
    var errorBody = new ErrorResponse($"ระบบยังตั้งค่าฐานข้อมูลไม่ครบ: {dbConfigError}");
    app.UseDefaultFiles();
    app.UseStaticFiles(); // หน้าเว็บยังเปิดได้ และจะแสดงข้อความ error จาก API
    // ทุก request อื่น (รวม /health และ /api/*) → 503 พร้อมข้อความ
    app.Run(ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return ctx.Response.WriteAsJsonAsync(errorBody);
    });
    await app.RunAsync();
    return;
}

if (importMode)
{
    var source = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(app.Environment.ContentRootPath, "worklog.db");
    Console.WriteLine($"ย้ายข้อมูล: {source}  →  {dbSettings}");
    using var importScope = app.Services.CreateScope();
    Environment.ExitCode = await SqliteToPostgresImporter.RunAsync(
        importScope.ServiceProvider.GetRequiredService<AppDbContext>(), source, Console.Out);
    return;
}

// ---------- เตรียมฐานข้อมูล: migrate เฉพาะเมื่อมี migration ค้าง + seed เฉพาะเมื่อตารางว่าง ----------
using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(), app.Configuration, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevCors");
}

// health check แบบเบา ไม่แตะฐานข้อมูล (ใช้กับ Vercel / Render / uptime monitor)
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    database = dbSettings!.Provider.ToString(),
    uptimeSeconds = (int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
}));

app.UseSwagger();
app.UseSwaggerUI(c => c.DocumentTitle = "Work Time Log API");

app.UseDefaultFiles();
app.UseStaticFiles();

// ---------- แอดมิน: ปิดทั้งหมด (503) ถ้าไม่มี ADMIN_PASSWORD → ตรวจ cookie → ตรวจสิทธิ์ ----------
app.UseAdminGate();
app.UseAuthentication();
app.UseAuthorization();
app.MapAdmin();

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine();
    Console.WriteLine("==============================================");
    Console.WriteLine($" ระบบบันทึกเวลาปฏิบัติงาน พร้อมใช้งาน (เริ่มระบบใน {startupClock.ElapsedMilliseconds} ms)");
    Console.WriteLine($"  ฐานข้อมูล    : {dbSettings}"); // ไม่มีรหัสผ่าน
    Console.WriteLine($"  บนเครื่องนี้ : http://localhost:{port}");
    Console.WriteLine($"  Swagger      : http://localhost:{port}/swagger");
    foreach (var ip in GetLanAddresses())
        Console.WriteLine($"  บนมือถือ     : http://{ip}:{port}");
    Console.WriteLine(" กด Ctrl+C เพื่อหยุด");
    Console.WriteLine("==============================================");
});

app.Run();

static IEnumerable<string> GetLanAddresses()
{
    try
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString())
            .Where(ip => !ip.StartsWith("169.254."))
            .Distinct()
            .ToList();
    }
    catch
    {
        return Array.Empty<string>(); // บาง sandbox (เช่น serverless) อ่าน network interface ไม่ได้
    }
}

public partial class Program { }
