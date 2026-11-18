using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Filters;
using WorkTimeLog.Api.Services;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// ---------- Kestrel: ฟังที่ http://0.0.0.0:{PORT} ----------
// Render (และ host อื่น ๆ) กำหนดพอร์ตผ่าน environment variable PORT ถ้าไม่มีใช้ 5000
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var envPort)
    ? envPort
    : builder.Configuration.GetValue("Port", 5000);
builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Any, port));
// ไม่ใช้ UseHttpsRedirection: Render / Cloudflare ทำ HTTPS ที่ด้านหน้าแล้วส่งต่อมาเป็น HTTP

// ---------- Database (SQLite) ----------
// path อ่านจาก environment variable DB_PATH ถ้าไม่มีใช้ connection string เดิม (worklog.db ในโฟลเดอร์โปรเจกต์ API)
var cs = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=worklog.db");
var dbPath = Environment.GetEnvironmentVariable("DB_PATH");
if (!string.IsNullOrWhiteSpace(dbPath))
    cs.DataSource = dbPath;
if (!Path.IsPathRooted(cs.DataSource))
    cs.DataSource = Path.Combine(builder.Environment.ContentRootPath, cs.DataSource);
var dbDir = Path.GetDirectoryName(cs.DataSource);
if (!string.IsNullOrEmpty(dbDir))
    Directory.CreateDirectory(dbDir);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(cs.ToString()));

// ---------- Services ----------
builder.Services.AddScoped<TimeEntryService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<ProjectService>();

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

// ---------- รัน migration อัตโนมัติตอนเริ่มแอป ----------
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
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
    Console.WriteLine($"  ฐานข้อมูล    : {cs.DataSource}");
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
