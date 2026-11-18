namespace WorkTimeLog.Api.Dtos;

/// <summary>จำนวนแถวของตารางหนึ่งในฐานข้อมูล</summary>
public record TableCountDto(string Name, long Rows);

/// <summary>ข้อมูลภาพรวมของฐานข้อมูล</summary>
public record DatabaseInfoDto(
    string DatabaseType,
    string? EngineVersion,
    string FilePath,
    long FileSizeBytes,
    long WalSizeBytes,
    IReadOnlyList<TableCountDto> Tables,
    int ProjectCount,
    int EntryCount,
    int ActiveEntryCount,
    int CancelledCount,
    int TotalMinutes,
    DateTime GeneratedAt);

/// <summary>แถวของตาราง Projects พร้อมสถิติ</summary>
public record DbProjectRowDto(int Id, string Name, bool IsActive, int EntryCount, int TotalMinutes);

/// <summary>ผลรวมนาทีต่อวัน (ไม่นับรายการที่ยกเลิก)</summary>
public record DailyTotalDto(
    DateOnly Date,
    int TotalMinutes,
    int RemainingMinutes,
    int EntryCount,
    int CancelledCount,
    bool IsOverLimit);
