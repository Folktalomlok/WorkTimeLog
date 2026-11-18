using Microsoft.AspNetCore.Mvc;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Produces("application/json")]
public class ProjectsController : ControllerBase
{
    private readonly ProjectService _service;

    public ProjectsController(ProjectService service) => _service = service;

    /// <summary>รายชื่อโครงการทั้งหมด</summary>
    [HttpGet]
    public Task<List<ProjectDto>> GetAll() => _service.GetAllAsync();

    /// <summary>เพิ่มโครงการใหม่ (ชื่อห้ามซ้ำ)</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectDto>> Create([FromBody] ProjectCreateDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return Created($"/api/projects/{created.Id}", created);
    }
}
