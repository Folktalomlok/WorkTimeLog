using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Filters;
using WorkTimeLog.Api.Services;

Console.OutputEncoding = Encoding.UTF8;

// โหมดย้ายข้อมูล: dotnet run --project src/WorkTimeLog.Api -- import-sqlite [path ไฟล์ .db]
var importMode = args.Length > 0 && args[0] == "import-sqlite";
var builder = WebApplication.CreateBuilder(importMode ? Array.Empty<string>() : args);

// ---------- Kestrel: ฟังที่ http://0.0.0.0:{PORT} ----------
// Render (และ host อื่น ๆ) กำหนดพอร์ตผ่าน environment variable PORT ถ้าไม่มีใช้ 5000
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var envPort)
    ? envPort
    : builder.Configuration.GetValue("Port", 5000);
builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Any, port));
// ไม่ใช้ UseHttpsRedirection: Render / Cloudflare ทำ HTTPS ที่ด้านหน้าแล้วส่งต่อมาเป็น HTTP

// ---------- Database: PostgreSQL (Neon) หรือ SQLite ----------
// ดู DatabaseSetup.Resolve: DB_PROVIDER / ConnectionStrings:Default (user-secrets หรือ env ConnectionStrings__Default) / DB_PATH
var dbSettings = DatabaseSetup.Resolve(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddAppDatabase(dbSettings);

// ---------- Services ----------
builder.Services.AddScoped<TimeEntryService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<DatabaseService>();

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

if (importMode)
{
    var source = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(app.Environment.ContentRootPath, "worklog.db");
    Console.WriteLine($"ย้ายข้อมูล: {source}  →  {dbSettings}");
    using var importScope = app.Services.CreateScope();
    Environment.ExitCode = await SqliteToPostgresImporter.RunAsync(
        importScope.ServiceProvider.GetRequiredService<AppDbContext>(), source, Console.Out);
    return;
}

// ---------- รัน migration อัตโนมัติ และใส่ seed data เฉพาะเมื่อตารางว่าง ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (await DbSeeder.SeedIfEmptyAsync(db))
        app.Logger.LogInformation("ใส่ข้อมูลตัวอย่างแล้ว (ตารางว่าง)");
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevCors");
}

app.UseSwagger();
app.UseSwaggerUI(c => c.DocumentTitle = "Work Time Log API");

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine();
    Console.WriteLine("==============================================");
    Console.WriteLine(" ระบบบันทึกเวลาปฏิบัติงาน พร้อมใช้งาน");
    Console.WriteLine($"  ฐานข้อมูล    : {dbSettings}"); // ไม่มีรหัสผ่าน
    Console.WriteLine($"  บนเครื่องนี้ : http://localhost:{port}");
    Console.WriteLine($"  Swagger      : http://localhost:{port}/swagger");
    foreach (var ip in GetLanAddresses())
        Console.WriteLine($"  บนมือถือ     : http://{ip}:{port}");
    Console.WriteLine(" กด Ctrl+C เพื่อหยุด");
    Console.WriteLine("==============================================");
});

app.Run();

static IEnumerable<string> GetLanAddresses() =>
    NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
        .Select(a => a.Address.ToString())
        .Where(ip => !ip.StartsWith("169.254."))
        .Distinct();

public partial class Program { }
