-- =============================================================
-- ระบบบันทึกเวลาปฏิบัติงาน (Work Time Log) — SQLite schema
-- ไฟล์ฐานข้อมูลจริง: src/WorkTimeLog.Api/worklog.db
-- (แอปสร้างตารางให้อัตโนมัติด้วย EF Core Migration "InitialCreate"
--  ไฟล์นี้ใช้ประกอบรายงาน / สร้างฐานข้อมูลด้วยมือ)
-- =============================================================

PRAGMA foreign_keys = ON;

-- ตารางโครงการ
CREATE TABLE IF NOT EXISTS Projects (
    Id        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name      TEXT    NOT NULL COLLATE NOCASE
              CHECK (length(Name) BETWEEN 1 AND 100),       -- ยาวไม่เกิน 100
    IsActive  INTEGER NOT NULL DEFAULT 1                    -- bool: 1 = ใช้งาน
);

-- ชื่อโครงการห้ามซ้ำ (ไม่สนตัวพิมพ์เล็ก/ใหญ่)
CREATE UNIQUE INDEX IF NOT EXISTS IX_Projects_Name ON Projects (Name);

-- ตารางรายการบันทึกเวลา
CREATE TABLE IF NOT EXISTS TimeEntries (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    WorkDate     TEXT    NOT NULL,                          -- DateOnly รูปแบบ 'YYYY-MM-DD'
    ProjectId    INTEGER NOT NULL,
    Description  TEXT    NOT NULL CHECK (length(Description) <= 500),
    Minutes      INTEGER NOT NULL,
    Note         TEXT    NULL     CHECK (Note IS NULL OR length(Note) <= 500),
    IsCancelled  INTEGER NOT NULL DEFAULT 0,                -- bool: 1 = ยกเลิก (soft cancel)
    CreatedAt    TEXT    NOT NULL,                          -- DateTime (UTC)
    UpdatedAt    TEXT    NOT NULL,                          -- DateTime (UTC)
    CONSTRAINT CK_TimeEntries_Minutes CHECK (Minutes > 0 AND Minutes <= 480),
    CONSTRAINT FK_TimeEntries_Projects_ProjectId
        FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_TimeEntries_WorkDate  ON TimeEntries (WorkDate);
CREATE INDEX IF NOT EXISTS IX_TimeEntries_ProjectId ON TimeEntries (ProjectId);

-- หมายเหตุ: กติกา "เวลารวมต่อวันไม่เกิน 480 นาที (ไม่นับรายการที่ยกเลิก)"
-- ตรวจในชั้น Service (TimeEntryService) เพราะต้องรวมหลายแถวและแสดงข้อความภาษาไทยได้

-- =============================================================
-- Seed data
-- =============================================================
INSERT INTO Projects (Id, Name, IsActive) VALUES
    (1, 'โครงงานคอมพิวเตอร์', 1),
    (2, 'ฝึกงาน / สหกิจศึกษา', 1),
    (3, 'ทบทวนบทเรียนและการบ้าน', 1);

INSERT INTO TimeEntries (Id, WorkDate, ProjectId, Description, Minutes, Note, IsCancelled, CreatedAt, UpdatedAt) VALUES
    (1, '2026-11-17', 1, 'ออกแบบฐานข้อมูลและ ER Diagram', 120, NULL,                         0, '2026-11-17 09:00:00', '2026-11-17 09:00:00'),
    (2, '2026-11-17', 2, 'ประชุมทีมประจำสัปดาห์',          60, 'ประชุมออนไลน์',              0, '2026-11-17 09:00:00', '2026-11-17 09:00:00'),
    (3, '2026-11-17', 3, 'ทำแบบฝึกหัดคณิตศาสตร์',          90, 'ตัวอย่างรายการที่ถูกยกเลิก', 1, '2026-11-17 09:00:00', '2026-11-17 09:00:00'),
    (4, '2026-11-18', 1, 'เขียน Web API ด้วย ASP.NET Core', 180, NULL,                        0, '2026-11-18 09:00:00', '2026-11-18 09:00:00'),
    (5, '2026-11-18', 2, 'จัดทำเอกสารรายงานประจำวัน',       90, 'รออาจารย์ตรวจ',             0, '2026-11-18 09:00:00', '2026-11-18 09:00:00');

-- ตัวอย่างคำสั่งตรวจเวลารวมต่อวัน (ไม่นับรายการที่ยกเลิก)
-- SELECT WorkDate, SUM(Minutes) AS TotalMinutes, 480 - SUM(Minutes) AS Remaining
-- FROM TimeEntries WHERE IsCancelled = 0 GROUP BY WorkDate ORDER BY WorkDate;
