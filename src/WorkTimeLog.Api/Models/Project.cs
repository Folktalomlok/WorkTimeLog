namespace WorkTimeLog.Api.Models;

/// <summary>โครงการ/งานที่ใช้จัดกลุ่มรายการบันทึกเวลา</summary>
public class Project
{
    public int Id { get; set; }

    /// <summary>ชื่อโครงการ (ห้ามซ้ำ, ไม่เกิน 100 ตัวอักษร)</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public List<TimeEntry> TimeEntries { get; set; } = new();
}
