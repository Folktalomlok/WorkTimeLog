using WorkTimeLog.Api.Models;

namespace WorkTimeLog.Api.Dtos;

// ---------- Projects ----------
public record ProjectDto(int Id, string Name, bool IsActive);

public class ProjectCreateDto
{
    public string? Name { get; set; }
}

// ---------- Time entries ----------
public record TimeEntryDto(
    int Id,
    DateOnly WorkDate,
    int ProjectId,
    string ProjectName,
    string Description,
    int Minutes,
    string? Note,
    bool IsCancelled,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>ข้อมูลที่ใช้ทั้งเพิ่ม (POST) และแก้ไข (PUT) รายการ</summary>
public class TimeEntryUpsertDto
{
    public DateOnly? WorkDate { get; set; }
    public int? ProjectId { get; set; }
    public string? Description { get; set; }
    public int? Minutes { get; set; }
    public string? Note { get; set; }
}

// ---------- Summary ----------
public record DailySummaryDto(
    DateOnly Date,
    int TotalMinutes,
    int RemainingMinutes,
    int MaxMinutes,
    int EntryCount,
    int CancelledCount);

public record ProjectSummaryItemDto(int ProjectId, string ProjectName, int TotalMinutes, int EntryCount);

public record ProjectSummaryDto(DateOnly From, DateOnly To, int TotalMinutes, IReadOnlyList<ProjectSummaryItemDto> Items);

// ---------- Error ----------
public record ErrorResponse(string Message);

public static class MappingExtensions
{
    public static ProjectDto ToDto(this Project p) => new(p.Id, p.Name, p.IsActive);

    public static TimeEntryDto ToDto(this TimeEntry e) => new(
        e.Id, e.WorkDate, e.ProjectId, e.Project?.Name ?? string.Empty,
        e.Description, e.Minutes, e.Note, e.IsCancelled, e.CreatedAt, e.UpdatedAt);
}
