using Microsoft.AspNetCore.Mvc;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Api.Controllers;

[ApiController]
[Route("api/entries")]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
public class EntriesController : ControllerBase
{
    private readonly TimeEntryService _service;

    public EntriesController(TimeEntryService service) => _service = service;

    /// <summary>
    /// รายการบันทึกเวลา: ใช้ ?date=YYYY-MM-DD (วันเดียว) หรือ ?from=YYYY-MM-DD&amp;to=YYYY-MM-DD (ช่วงวันที่)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TimeEntryDto>>> Get(
        [FromQuery] DateOnly? date, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        if (date is not null)
            return await _service.GetByDateAsync(date.Value);
        if (from is not null && to is not null)
            return await _service.GetRangeAsync(from.Value, to.Value);
        throw new BusinessRuleException("กรุณาระบุ date หรือ from และ to (รูปแบบ YYYY-MM-DD)");
    }

    /// <summary>ดูรายการเดียว</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public Task<TimeEntryDto> GetById(int id) => _service.GetAsync(id);

    /// <summary>เพิ่มรายการ</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TimeEntryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TimeEntryDto>> Create([FromBody] TimeEntryUpsertDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>แก้ไขรายการ</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public Task<TimeEntryDto> Update(int id, [FromBody] TimeEntryUpsertDto dto) => _service.UpdateAsync(id, dto);

    /// <summary>ยกเลิกรายการ (soft cancel)</summary>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public Task<TimeEntryDto> Cancel(int id) => _service.CancelAsync(id);

    /// <summary>กู้คืนรายการที่ยกเลิก (ตรวจกติกา 480 นาทีอีกครั้ง)</summary>
    [HttpPost("{id:int}/restore")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public Task<TimeEntryDto> Restore(int id) => _service.RestoreAsync(id);
}
