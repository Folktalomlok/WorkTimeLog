namespace WorkTimeLog.Api.Models;

/// <summary>รายการบันทึกเวลาปฏิบัติงาน 1 รายการ</summary>
public class TimeEntry
{
    public int Id { get; set; }

    /// <summary>วันที่ปฏิบัติงาน</summary>
    public DateOnly WorkDate { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>รายละเอียดงาน (ไม่เกิน 500 ตัวอักษร)</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>เวลาที่ใช้ (นาที) ต้องมากกว่า 0 และไม่เกิน 480</summary>
    public int Minutes { get; set; }

    /// <summary>หมายเหตุ (ไม่บังคับ, ไม่เกิน 500 ตัวอักษร)</summary>
    public string? Note { get; set; }

    /// <summary>สถานะยกเลิก (soft cancel) — รายการที่ยกเลิกจะไม่ถูกนับในเวลารวม</summary>
    public bool IsCancelled { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
