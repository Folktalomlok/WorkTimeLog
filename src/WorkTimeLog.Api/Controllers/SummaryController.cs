using Microsoft.AspNetCore.Mvc;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Api.Controllers;

[ApiController]
[Route("api/summary")]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
public class SummaryController : ControllerBase
{
    private readonly SummaryService _service;

    public SummaryController(SummaryService service) => _service = service;

    /// <summary>สรุปรายวัน: เวลารวม, เวลาที่เหลือจาก 480, จำนวนรายการ</summary>
    [HttpGet("daily")]
    public Task<DailySummaryDto> Daily([FromQuery] DateOnly? date)
    {
        if (date is null) throw new BusinessRuleException("กรุณาระบุ date (รูปแบบ YYYY-MM-DD)");
        return _service.GetDailyAsync(date.Value);
    }

    /// <summary>เวลารวมแยกตามโครงการในช่วงวันที่</summary>
    [HttpGet("by-project")]
    public Task<ProjectSummaryDto> ByProject([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        if (from is null || to is null) throw new BusinessRuleException("กรุณาระบุ from และ to (รูปแบบ YYYY-MM-DD)");
        return _service.GetByProjectAsync(from.Value, to.Value);
    }
}
