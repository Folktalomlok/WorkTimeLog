using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkTimeLog.Api.Admin;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Api.Controllers;

/// <summary>
/// ดูข้อมูลในฐานข้อมูล — อ่านอย่างเดียว (มีแค่ GET ไม่มี endpoint แก้ไข/ลบ)
/// </summary>
[ApiController]
[Route("api/database")]
[Authorize(Policy = AdminAuth.PolicyName)] // ทุก endpoint ต้องล็อกอินแอดมินก่อน (ไม่ล็อกอิน → 401)
public class DatabaseController : ControllerBase
{
    private readonly DatabaseService _service;
    private readonly ILogger<DatabaseController> _logger;

    public DatabaseController(DatabaseService service, ILogger<DatabaseController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>ชนิดฐานข้อมูล, path, ขนาดไฟล์, จำนวนแถวแต่ละตาราง, เวลารวม และจำนวนรายการที่ยกเลิก</summary>
    [HttpGet("info")]
    [Produces("application/json")]
    public Task<DatabaseInfoDto> Info() => _service.GetInfoAsync();

    /// <summary>ทุกแถวของตาราง Projects</summary>
    [HttpGet("projects")]
    [Produces("application/json")]
    public Task<List<DbProjectRowDto>> Projects() => _service.GetProjectsAsync();

    /// <summary>ทุกแถวของตาราง TimeEntries (รวมรายการที่ยกเลิก) พร้อมชื่อโครงการ เรียง WorkDate ล่าสุดก่อน</summary>
    [HttpGet("entries")]
    [Produces("application/json")]
    public Task<List<TimeEntryDto>> Entries() => _service.GetEntriesAsync();

    /// <summary>ผลรวมนาทีต่อวัน (ไม่นับรายการที่ยกเลิก), เวลาที่เหลือจาก 480 และสถานะเกินหรือไม่</summary>
    [HttpGet("daily-totals")]
    [Produces("application/json")]
    public Task<List<DailyTotalDto>> DailyTotals() => _service.GetDailyTotalsAsync();

    /// <summary>ดาวน์โหลด TimeEntries เป็น CSV (UTF-8 with BOM)</summary>
    [HttpGet("export/entries.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportEntriesCsv()
    {
        var bytes = await _service.ExportEntriesCsvAsync();
        return File(bytes, "text/csv; charset=utf-8", $"worklog-entries-{Stamp()}.csv");
    }

    /// <summary>ดาวน์โหลดสำเนาไฟล์ worklog.db (checkpoint WAL ก่อน แล้วส่งไฟล์สำเนาชั่วคราว)</summary>
    [HttpGet("download")]
    [Produces("application/octet-stream")]
    public async Task<IActionResult> Download()
    {
        if (!_service.CanDownloadFile)
            throw new BusinessRuleException("ดาวน์โหลดไฟล์ .db ได้เฉพาะเมื่อใช้ SQLite — ฐานข้อมูล PostgreSQL ให้ใช้ปุ่มส่งออก CSV แทน");

        var temp = await _service.CreateDatabaseCopyAsync();
        _logger.LogInformation("Database copy created for download: {Temp}", temp);
        // DeleteOnClose: ไฟล์ชั่วคราวถูกลบอัตโนมัติเมื่อส่งเสร็จ
        var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        return File(stream, "application/octet-stream", $"worklog-{Stamp()}.db");
    }

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
}
