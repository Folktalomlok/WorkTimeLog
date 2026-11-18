-- =====================================================================================
--  ระบบบันทึกเวลาปฏิบัติงาน (Work Time Log) — สคริปต์ฐานข้อมูล MySQL 8
-- -------------------------------------------------------------------------------------
--  ผู้จัดทำ    : นายกิตติศักดิ์ ชละเอม
--  ใช้กับ      : MySQL 8.0.16 ขึ้นไป (InnoDB, utf8mb4 — CHECK constraint บังคับใช้ตั้งแต่ 8.0.16)
--  โครงสร้าง  : แปลงจาก EF Core Migrations ของแอป (ระบบจริงใช้ PostgreSQL บน Neon)
--  ข้อมูลตัวอย่าง : ข้อมูลสมมติสำหรับสาธิตเท่านั้น (ไม่ใช่ข้อมูลจริงของระบบ)
--
--  ความปลอดภัยของสคริปต์: ทำงานในฐานข้อมูล `worktimelog_demo` เท่านั้น
--    ลบทั้งหมดหลังทดลอง:  DROP DATABASE worktimelog_demo;
--  วิธีรัน:  mysql -u root -p < worktimelog_mysql.sql   (หรือเปิดใน MySQL Workbench แล้วรันทั้งไฟล์)
--
--  ตารางของระบบที่ "ไม่ได้" ใส่ไว้ (แอปสร้างเองด้วย EF Core)
--    * __EFMigrationsHistory — EF Core จดว่า migration ใดถูก apply แล้ว
--    * DataProtectionKeys    — key ring ของ ASP.NET Core Data Protection (เข้ารหัส cookie ล็อกอินแอดมิน
--                              เก็บในฐานข้อมูลเพื่อให้ทุก instance ใช้ key ชุดเดียวกัน)
--
--  กติกา: เวลารวมต่อวันไม่เกิน 480 นาที (ไม่นับรายการที่ยกเลิก), แก้ไขแล้วใช้ค่าใหม่แทนค่าเดิมก่อนรวม,
--         ยกเลิกแบบ soft cancel กู้คืนได้ (ตรวจ 480 อีกครั้ง), Minutes 1–480 — ตรวจในชั้น Service ของแอป
-- =====================================================================================

CREATE DATABASE IF NOT EXISTS worktimelog_demo
    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_ci;
USE worktimelog_demo;

SET NAMES utf8mb4;

DROP TABLE IF EXISTS TimeEntries;
DROP TABLE IF EXISTS Projects;
DROP TABLE IF EXISTS AdminLoginAttempts;

-- ---------- Projects ----------
-- Name ใช้ utf8mb4_0900_as_ci: ไม่สนตัวพิมพ์เล็ก/ใหญ่ (เทียบเท่า NOCASE / ICU ของ PostgreSQL)
-- แต่สนวรรณยุกต์ (accent-sensitive) เพื่อไม่ให้ชื่อภาษาไทยที่ต่างกันแค่วรรณยุกต์ถือว่าซ้ำ
CREATE TABLE Projects (
    Id       INT          NOT NULL AUTO_INCREMENT                         COMMENT 'รหัสโครงการ (Primary Key, เพิ่มอัตโนมัติ)',
    Name     VARCHAR(100) NOT NULL COLLATE utf8mb4_0900_as_ci             COMMENT 'ชื่อโครงการ ห้ามซ้ำ (ไม่สนตัวพิมพ์เล็ก/ใหญ่) ยาวไม่เกิน 100 ตัวอักษร',
    IsActive TINYINT(1)   NOT NULL DEFAULT 1                              COMMENT 'สถานะใช้งาน: 1 = เลือกได้ในฟอร์ม, 0 = ปิดใช้งาน',
    CONSTRAINT PK_Projects PRIMARY KEY (Id),
    CONSTRAINT IX_Projects_Name UNIQUE (Name)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_as_ci
  COMMENT = 'โครงการ/งานที่ใช้จัดกลุ่มรายการบันทึกเวลา';

-- ---------- TimeEntries ----------
CREATE TABLE TimeEntries (
    Id          INT          NOT NULL AUTO_INCREMENT                      COMMENT 'รหัสรายการ (Primary Key, เพิ่มอัตโนมัติ)',
    WorkDate    DATE         NOT NULL                                     COMMENT 'วันที่ปฏิบัติงาน (มี index)',
    ProjectId   INT          NOT NULL                                     COMMENT 'โครงการ (Foreign Key → Projects.Id)',
    Description VARCHAR(500) NOT NULL                                     COMMENT 'รายละเอียดงาน ห้ามว่าง ยาวไม่เกิน 500 ตัวอักษร',
    Minutes     INT          NOT NULL                                     COMMENT 'เวลาที่ใช้ (นาที) ต้องอยู่ระหว่าง 1–480',
    Note        VARCHAR(500) NULL                                         COMMENT 'หมายเหตุ (ไม่บังคับ) ยาวไม่เกิน 500 ตัวอักษร',
    IsCancelled TINYINT(1)   NOT NULL DEFAULT 0                           COMMENT 'ยกเลิกแบบ soft cancel: 1 = ยกเลิก (ไม่นับในเวลารวม กู้คืนได้)',
    CreatedAt   DATETIME(6)  NOT NULL                                     COMMENT 'วันเวลาที่สร้างรายการ (UTC)',
    UpdatedAt   DATETIME(6)  NOT NULL                                     COMMENT 'วันเวลาที่แก้ไขล่าสุด (UTC)',
    CONSTRAINT PK_TimeEntries PRIMARY KEY (Id),
    CONSTRAINT CK_TimeEntries_Minutes CHECK (Minutes > 0 AND Minutes <= 480),
    CONSTRAINT FK_TimeEntries_Projects_ProjectId FOREIGN KEY (ProjectId)
        REFERENCES Projects (Id) ON DELETE RESTRICT,
    INDEX IX_TimeEntries_WorkDate (WorkDate),
    INDEX IX_TimeEntries_ProjectId (ProjectId)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_as_ci
  COMMENT = 'รายการบันทึกเวลาปฏิบัติงาน (เวลารวมต่อวันไม่เกิน 480 นาที ไม่นับรายการที่ยกเลิก)';

-- ---------- AdminLoginAttempts ----------
CREATE TABLE AdminLoginAttempts (
    Id          BIGINT      NOT NULL AUTO_INCREMENT                       COMMENT 'รหัสรายการ (Primary Key)',
    IpAddress   VARCHAR(64) NOT NULL                                      COMMENT 'IP ของผู้ที่ใส่รหัสแอดมินผิด',
    AttemptedAt DATETIME(6) NOT NULL                                      COMMENT 'วันเวลาที่ใส่รหัสผิด (UTC)',
    CONSTRAINT PK_AdminLoginAttempts PRIMARY KEY (Id),
    INDEX IX_AdminLoginAttempts_IpAddress_AttemptedAt (IpAddress, AttemptedAt)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_as_ci
  COMMENT = 'การใส่รหัสแอดมินผิด ใช้จำกัด 5 ครั้งต่อ 15 นาทีต่อ IP';

-- =====================================================================================
-- ข้อมูลตัวอย่าง (สมมติ) — สัปดาห์ฝึกงาน 2–6 พฤศจิกายน 2026
-- =====================================================================================
INSERT INTO Projects (Id, Name, IsActive) VALUES
    (1, 'พัฒนาระบบบันทึกเวลาปฏิบัติงาน', 1),
    (2, 'งานสนับสนุนผู้ใช้ฝ่ายไอที',      1),
    (3, 'เรียนรู้ .NET และ PostgreSQL',   1),
    (4, 'ประชุมและกิจกรรมองค์กร',         1),
    (5, 'ปรับปรุงเว็บไซต์ภายใน (ปิดแล้ว)', 0);

INSERT INTO TimeEntries (Id, WorkDate, ProjectId, Description, Minutes, Note, IsCancelled, CreatedAt, UpdatedAt) VALUES
    ( 1, '2026-11-02', 4, 'ปฐมนิเทศและรับฟังกฎระเบียบความปลอดภัยของบริษัท',      120, NULL,                        0, '2026-11-02 02:00:00', '2026-11-02 02:00:00'),
    ( 2, '2026-11-02', 1, 'วิเคราะห์ความต้องการและออกแบบฐานข้อมูล (ER Diagram)', 180, 'ส่งร่างให้พี่เลี้ยงตรวจแล้ว', 0, '2026-11-02 06:30:00', '2026-11-02 06:30:00'),
    ( 3, '2026-11-02', 3, 'ศึกษา Entity Framework Core เรื่อง Migrations',         120, NULL,                        0, '2026-11-02 09:00:00', '2026-11-02 09:00:00'),
    ( 4, '2026-11-03', 1, 'เขียน Web API สำหรับเพิ่ม/แก้ไขรายการบันทึกเวลา',      240, NULL,                        0, '2026-11-03 05:00:00', '2026-11-03 05:00:00'),
    ( 5, '2026-11-03', 2, 'ติดตั้งเครื่องพิมพ์และแก้ปัญหาเครือข่ายให้ฝ่ายบัญชี',  150, 'รอเปลี่ยนสาย LAN ชั้น 2',   0, '2026-11-03 08:00:00', '2026-11-03 08:00:00'),
    ( 6, '2026-11-03', 4, 'ประชุมทีมประจำสัปดาห์',                                 90, NULL,                        0, '2026-11-03 09:45:00', '2026-11-03 09:45:00'),
    ( 7, '2026-11-04', 1, 'ทำหน้าจอบันทึกเวลาแบบ responsive สำหรับมือถือ',       210, NULL,                        0, '2026-11-04 05:30:00', '2026-11-04 05:30:00'),
    ( 8, '2026-11-04', 3, 'ทดลองเชื่อมต่อ PostgreSQL บนคลาวด์ (Neon)',            90, NULL,                        0, '2026-11-04 07:00:00', '2026-11-04 07:00:00'),
    ( 9, '2026-11-04', 2, 'อบรมการใช้งานโปรแกรมให้พนักงานใหม่',                   120, 'เลื่อนเป็นสัปดาห์หน้า',      1, '2026-11-04 08:00:00', '2026-11-04 10:15:00'),
    (10, '2026-11-04', 4, 'สรุปผลการทำงานรายวันกับพี่เลี้ยง',                      90, NULL,                        0, '2026-11-04 10:00:00', '2026-11-04 10:00:00'),
    (11, '2026-11-05', 1, 'เขียน Unit Test กติกาเวลารวมไม่เกิน 480 นาที',          180, 'ผ่านครบ 8 กรณี',            0, '2026-11-05 05:00:00', '2026-11-05 05:00:00'),
    (12, '2026-11-05', 2, 'สำรองข้อมูลและตรวจสอบเครื่องลูกข่าย',                   120, NULL,                        0, '2026-11-05 07:30:00', '2026-11-05 07:30:00'),
    (13, '2026-11-05', 3, 'ศึกษาการ deploy ด้วย Docker',                             60, NULL,                        0, '2026-11-05 09:00:00', '2026-11-05 09:00:00'),
    (14, '2026-11-06', 1, 'Deploy ระบบขึ้นคลาวด์และทดสอบบนมือถือ',                 150, NULL,                        0, '2026-11-06 05:00:00', '2026-11-06 05:00:00'),
    (15, '2026-11-06', 1, 'จัดทำคู่มือการใช้งานระบบ',                               150, 'ส่งฉบับร่างแล้ว',            0, '2026-11-06 08:00:00', '2026-11-06 08:00:00');

-- =====================================================================================
-- Query ตัวอย่าง
-- =====================================================================================

-- Q1) เวลารวมต่อวัน (ไม่นับรายการที่ยกเลิก) + เวลาที่เหลือจาก 480
SELECT  WorkDate                                                  AS `วันที่`,
        SUM(CASE WHEN IsCancelled = 0 THEN Minutes ELSE 0 END)     AS `เวลารวม_นาที`,
        480 - SUM(CASE WHEN IsCancelled = 0 THEN Minutes ELSE 0 END) AS `เหลือได้อีก_นาที`,
        SUM(IsCancelled = 0)                                      AS `จำนวนรายการ`,
        SUM(IsCancelled = 1)                                      AS `ยกเลิก`
FROM    TimeEntries
GROUP BY WorkDate
ORDER BY WorkDate;

-- Q2) เวลารวมแยกตามโครงการในช่วงวันที่ (ไม่นับรายการที่ยกเลิก)
SELECT  p.Id AS `รหัสโครงการ`, p.Name AS `โครงการ`,
        SUM(t.Minutes) AS `เวลารวม_นาที`, ROUND(SUM(t.Minutes) / 60, 2) AS `เวลารวม_ชั่วโมง`, COUNT(*) AS `จำนวนรายการ`
FROM    TimeEntries t
JOIN    Projects p ON p.Id = t.ProjectId
WHERE   t.IsCancelled = 0
  AND   t.WorkDate BETWEEN '2026-11-02' AND '2026-11-06'
GROUP BY p.Id, p.Name
ORDER BY `เวลารวม_นาที` DESC;

-- Q3) ตรวจกติกา 480 นาที "ตอนแก้ไข": แก้รายการ Id 6 (วันที่ 3 พ.ย. ซึ่งเต็ม 480) เป็น 100 นาที
--     รวม = (รายการอื่นในวันใหม่ที่ไม่ถูกยกเลิก ไม่นับรายการที่กำลังแก้) + ค่าใหม่ = 390 + 100 = 490 → ไม่ผ่าน
SET @entry_id = 6, @new_work_date = '2026-11-03', @new_minutes = 100;
SELECT  @new_work_date                                       AS `วันที่ใหม่`,
        COALESCE(SUM(Minutes), 0)                            AS `รายการอื่น_นาที`,
        @new_minutes                                         AS `ค่าใหม่_นาที`,
        COALESCE(SUM(Minutes), 0) + @new_minutes             AS `รวมหลังแก้_นาที`,
        GREATEST(0, 480 - COALESCE(SUM(Minutes), 0))         AS `เหลือได้อีก_นาที`,
        IF(@new_minutes BETWEEN 1 AND 480 AND COALESCE(SUM(Minutes), 0) + @new_minutes <= 480, 'ผ่าน', 'ไม่ผ่าน') AS `ผลตรวจ`
FROM    TimeEntries
WHERE   WorkDate = @new_work_date AND IsCancelled = 0 AND Id <> @entry_id;

-- Q4) รายการของวันหนึ่ง พร้อมชื่อโครงการ (รวมรายการที่ยกเลิก)
SELECT  t.Id, t.WorkDate, p.Name AS `โครงการ`, t.Description, t.Minutes, t.Note,
        IF(t.IsCancelled = 1, 'ยกเลิก', 'ปกติ') AS `สถานะ`
FROM    TimeEntries t JOIN Projects p ON p.Id = t.ProjectId
WHERE   t.WorkDate = '2026-11-04'
ORDER BY t.CreatedAt;
