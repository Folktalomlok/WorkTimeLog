using Microsoft.EntityFrameworkCore;
using WorkTimeLog.Api.Data;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Services;

public class ProjectService
{
    public const int MaxNameLength = 100;

    private readonly AppDbContext _db;

    public ProjectService(AppDbContext db) => _db = db;

    public async Task<List<ProjectDto>> GetAllAsync()
    {
        var list = await _db.Projects.OrderBy(p => p.Name).ToListAsync();
        return list.Select(p => p.ToDto()).ToList();
    }

    public async Task<ProjectDto> CreateAsync(ProjectCreateDto? dto)
    {
        var name = dto?.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new BusinessRuleException("กรุณากรอกชื่อโครงการ");
        if (name.Length > MaxNameLength)
            throw new BusinessRuleException($"ชื่อโครงการต้องยาวไม่เกิน {MaxNameLength} ตัวอักษร");

        // คอลัมน์ Name ใช้ collation NOCASE จึงเทียบแบบไม่สนตัวพิมพ์เล็ก/ใหญ่
        if (await _db.Projects.AnyAsync(p => p.Name == name))
            throw new BusinessRuleException($"มีโครงการชื่อ \"{name}\" อยู่แล้ว");

        var project = new Project { Name = name, IsActive = true };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project.ToDto();
    }
}
