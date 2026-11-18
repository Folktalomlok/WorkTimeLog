namespace WorkTimeLog.Api.Dtos;

/// <summary>จำนวนแถวของตารางหนึ่งในฐานข้อมูล</summary>
public record TableCountDto(string Name, long Rows);

/// <summary>ข้อมูลภาพรวมของฐานข้อมูล (ไม่มีรหัสผ่านหรือ connection string)</summary>
public record DatabaseInfoDto(
    string DatabaseType,          // "SQLite" หรือ "PostgreSQL"
    string? EngineVersion,
    string? Host,                 // PostgreSQL เท่านั้น
    string? DatabaseName,         // PostgreSQL เท่านั้น
    string? FilePath,             // SQLite เท่านั้น
    long SizeBytes,               // SQLite: ขนาดไฟล์ / PostgreSQL: pg_database_size
    long WalSizeBytes,            // SQLite เท่านั้น
    bool CanDownloadFile,         // ดาวน์โหลดไฟล์ .db ได้ (SQLite ที่เป็นไฟล์)
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
