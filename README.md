# ระบบบันทึกเวลาปฏิบัติงาน (Work Time Log)

ระบบสำหรับนักศึกษา 1 คน ใช้บันทึกเวลาที่ใช้กับงาน/โครงการในแต่ละวัน จำกัดเวลารวมไม่เกิน **480 นาทีต่อวัน**
ใช้งานได้ทั้งบนคอมพิวเตอร์และมือถือ (responsive)

| ส่วน | เทคโนโลยี |
|---|---|
| Backend | ASP.NET Core Web API (.NET 8) ภาษา C# |
| Database | SQLite + Entity Framework Core 8 (Code-First + Migrations) ไฟล์ `worklog.db` |
| Frontend | HTML + CSS + JavaScript (fetch API) + Bootstrap 5 (mobile-first) อยู่ใน `wwwroot` |
| API Docs | Swagger (Swashbuckle) |
| Test | xUnit + SQLite in-memory |

---

## 1. โครงสร้างโปรเจกต์

```
WorkTimeLog/
├─ WorkTimeLog.sln
├─ .config/dotnet-tools.json         ← เครื่องมือ dotnet-ef (local tool)
├─ .vscode/                          ← tasks / launch สำหรับ VS Code
├─ src/WorkTimeLog.Api/
│  ├─ Models/            Project.cs, TimeEntry.cs              (Entity)
│  ├─ Data/              AppDbContext.cs (+ seed data)
│  │  └─ Migrations/     InitialCreate
│  ├─ Dtos/              Dtos.cs                               (DTO แยกจาก Entity)
│  ├─ Services/          TimeEntryService.cs  ← กติกาทางธุรกิจทั้งหมด
│  │                     SummaryService.cs, ProjectService.cs, Exceptions.cs
│  ├─ Filters/           ApiExceptionFilter.cs  (แปลง exception → HTTP 400/404)
│  ├─ Controllers/       ProjectsController, EntriesController, SummaryController
│  ├─ wwwroot/           index.html, css/site.css, js/app.js
│  ├─ Program.cs         Kestrel 0.0.0.0:5000, CORS, Swagger, auto-migrate
│  └─ appsettings.json
├─ tests/WorkTimeLog.Tests/
│  ├─ TestDb.cs                      ← SQLite in-memory
│  ├─ TimeEntryServiceTests.cs
│  └─ SummaryAndProjectTests.cs
├─ database/schema.sql               ← CREATE TABLE (SQLite) + seed
├─ docs/ER-diagram.md                ← Mermaid erDiagram
└─ README.md
```

---

## 2. การติดตั้ง

1. ติดตั้ง **.NET 8 SDK** — ตรวจสอบด้วย
   ```powershell
   dotnet --version      # ต้องขึ้นต้นด้วย 8.
   ```
   ถ้ายังไม่มี ติดตั้งด้วยคำสั่ง (PowerShell)
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```
   หรือดาวน์โหลดจาก <https://dotnet.microsoft.com/download/dotnet/8.0> แล้ว **ปิด–เปิด VS Code / Terminal ใหม่**
2. ติดตั้ง **VS Code** และ extension **C# Dev Kit** (VS Code จะแนะนำให้ติดตั้งอัตโนมัติเมื่อเปิดโฟลเดอร์)
3. เปิดโฟลเดอร์ `WorkTimeLog` ใน VS Code (File → Open Folder…)
4. คืนค่า package และเครื่องมือ
   ```powershell
   dotnet restore
   dotnet tool restore      # ติดตั้ง dotnet-ef ตามไฟล์ .config/dotnet-tools.json
   ```

> คำสั่งที่ใช้สร้างโปรเจกต์นี้ตั้งแต่ต้น (อ้างอิง)
> ```powershell
> dotnet new sln -n WorkTimeLog
> dotnet new webapi -n WorkTimeLog.Api -o src/WorkTimeLog.Api -f net8.0 --use-controllers
> dotnet new xunit  -n WorkTimeLog.Tests -o tests/WorkTimeLog.Tests -f net8.0
> dotnet sln add src/WorkTimeLog.Api tests/WorkTimeLog.Tests
> dotnet add src/WorkTimeLog.Api package Microsoft.EntityFrameworkCore.Sqlite -v 8.0.11
> dotnet add src/WorkTimeLog.Api package Microsoft.EntityFrameworkCore.Design -v 8.0.11
> dotnet add src/WorkTimeLog.Api package Swashbuckle.AspNetCore -v 6.6.2
> dotnet add tests/WorkTimeLog.Tests reference src/WorkTimeLog.Api
> dotnet add tests/WorkTimeLog.Tests package Microsoft.EntityFrameworkCore.Sqlite -v 8.0.11
> dotnet new tool-manifest
> dotnet tool install dotnet-ef --version 8.0.11
> dotnet ef migrations add InitialCreate --project src/WorkTimeLog.Api -o Data/Migrations
> ```

---

## 3. วิธีรัน

เปิด Terminal ใน VS Code (`` Ctrl+` ``) ที่โฟลเดอร์ `WorkTimeLog` แล้วพิมพ์

```powershell
dotnet run --project src/WorkTimeLog.Api
```

- ครั้งแรกโปรแกรมจะรัน migration อัตโนมัติ (`db.Database.Migrate()`) และสร้างไฟล์ `src/WorkTimeLog.Api/worklog.db` พร้อมข้อมูลตัวอย่าง
- หน้าเว็บ: <http://localhost:5000>
- Swagger: <http://localhost:5000/swagger>
- หยุดโปรแกรม: กด `Ctrl+C`
- หรือกด **F5** ใน VS Code (ใช้ `.vscode/launch.json`) / เมนู Terminal → Run Task → `run`

รัน Unit Test:

```powershell
dotnet test
```

ถ้าต้องการล้างข้อมูลทั้งหมดกลับเป็นข้อมูลตัวอย่าง: หยุดโปรแกรม แล้วลบไฟล์ `src/WorkTimeLog.Api/worklog.db` จากนั้นรันใหม่

---

## 4. วิธีเปิดบนมือถือ

แอปฟังที่ `http://0.0.0.0:5000` (ทุก network interface) มือถือที่ต่อ **Wi-Fi เดียวกัน** กับคอมพิวเตอร์จึงเข้าได้

### 4.1 หา IP ของเครื่องด้วย `ipconfig`

```powershell
ipconfig
```

ดูหัวข้อ **Wireless LAN adapter Wi-Fi** แล้วจดค่า **IPv4 Address** เช่น

```
Wireless LAN adapter Wi-Fi:
   IPv4 Address. . . . . . . . . . . : 192.168.1.45
```

> ตอนโปรแกรมเริ่มทำงาน จะพิมพ์ลิงก์ "บนมือถือ" ทุก IP ของเครื่องให้ด้วย
> ถ้ามีหลายรายการ ให้เลือก IP ของ Wi-Fi (ไม่ใช่ของ VirtualBox, VPN หรือ WSL เช่น `192.168.56.x`)

### 4.2 เปิด Windows Firewall port 5000

เปิด **PowerShell แบบ Run as Administrator** แล้วพิมพ์

```powershell
New-NetFirewallRule -DisplayName "WorkTimeLog 5000" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -Profile Private
```

หรือใช้คำสั่งแบบเดิม

```cmd
netsh advfirewall firewall add rule name="WorkTimeLog 5000" dir=in action=allow protocol=TCP localport=5000
```

> - ถ้า Wi-Fi ถูกตั้งเป็น **Public network** ให้เปลี่ยนเป็น **Private** (Settings → Network & internet → Wi-Fi → ชื่อ Wi-Fi → Network profile type → Private) หรือเปลี่ยน `-Profile Private` เป็น `-Profile Any`
> - ครั้งแรกที่รัน Windows อาจถามสิทธิ์เครือข่าย ให้กด **Allow**
> - ลบกฎเมื่อเลิกใช้: `Remove-NetFirewallRule -DisplayName "WorkTimeLog 5000"`
> - Wi-Fi ของสถานศึกษา/ที่สาธารณะบางแห่งเปิด *client isolation* ทำให้เครื่องคุยกันไม่ได้ ให้ใช้ Hotspot จากมือถือแทน

### 4.3 เปิดบนมือถือ

เปิดเบราว์เซอร์บนมือถือ แล้วพิมพ์ `http://<IP ของเครื่อง>:5000` เช่น `http://192.168.1.45:5000`
(มือถือต้องต่ออินเทอร์เน็ตได้ด้วย เพราะหน้าเว็บโหลด Bootstrap จาก CDN)

---

## 5. กติกาทางธุรกิจ

ตรวจใน `Services/TimeEntryService.cs` (ไม่อยู่ใน controller จึงทดสอบด้วย unit test ได้)

| # | กติกา |
|---|---|
| 1 | เวลารวมต่อวัน (WorkDate เดียวกัน) ต้องไม่เกิน 480 นาที |
| 2 | ไม่นับรายการที่ยกเลิก (`IsCancelled = true`) ในการรวมเวลา |
| 3 | ตอนแก้ไข: `เวลารวม = (ผลรวมรายการอื่นที่ไม่ถูกยกเลิกในวันใหม่ ไม่นับรายการที่กำลังแก้) + Minutes ใหม่` ถ้าย้ายวันที่จะตรวจกับวันใหม่ |
| 4 | ยกเลิกแบบ soft cancel (ไม่ลบจริง) กู้คืนได้ และตอนกู้คืนต้องตรวจกติกา 480 นาทีอีกครั้ง |
| 5 | Minutes ต้องมากกว่า 0 และไม่เกิน 480 |
| 6 | ผิดกติกา → HTTP 400 `{ "message": "..." }` ภาษาไทย เช่น `เวลารวมวันที่ 2026-11-18 จะเป็น 510 นาที เกินกำหนด 480 นาที (เหลือได้อีก 30 นาที)` |

กติกาเพิ่มเติม: รายละเอียดงานต้องไม่ว่างและยาวไม่เกิน 500, หมายเหตุยาวไม่เกิน 500, ชื่อโครงการห้ามซ้ำ (ไม่สนตัวพิมพ์เล็ก/ใหญ่), รายการที่ยกเลิกแล้วต้องกู้คืนก่อนจึงแก้ไขได้

---

## 6. รายการ API

Base URL: `http://localhost:5000` — ทดลองได้ที่ `/swagger` หรือไฟล์ `src/WorkTimeLog.Api/WorkTimeLog.Api.http`

| Method | Endpoint | คำอธิบาย |
|---|---|---|
| GET | `/api/projects` | รายชื่อโครงการทั้งหมด |
| POST | `/api/projects` | เพิ่มโครงการ — body `{ "name": "..." }` |
| GET | `/api/entries?date=YYYY-MM-DD` | รายการของวันนั้น (รวมรายการที่ยกเลิก) |
| GET | `/api/entries?from=YYYY-MM-DD&to=YYYY-MM-DD` | รายการในช่วงวันที่ |
| GET | `/api/entries/{id}` | ดูรายการเดียว |
| POST | `/api/entries` | เพิ่มรายการ |
| PUT | `/api/entries/{id}` | แก้ไขรายการ |
| POST | `/api/entries/{id}/cancel` | ยกเลิกรายการ (soft cancel) |
| POST | `/api/entries/{id}/restore` | กู้คืนรายการ (ตรวจ 480 นาทีอีกครั้ง) |
| GET | `/api/summary/daily?date=YYYY-MM-DD` | เวลารวม, เวลาที่เหลือจาก 480, จำนวนรายการ |
| GET | `/api/summary/by-project?from=...&to=...` | เวลารวมแยกตามโครงการ |

ตัวอย่าง body ของ POST/PUT `/api/entries`

```json
{
  "workDate": "2026-11-18",
  "projectId": 1,
  "description": "เขียนโปรแกรมส่วน API",
  "minutes": 90,
  "note": "ทำต่อพรุ่งนี้"
}
```

ตัวอย่างผลลัพธ์ `/api/summary/daily?date=2026-11-18`

```json
{ "date": "2026-11-18", "totalMinutes": 270, "remainingMinutes": 210, "maxMinutes": 480, "entryCount": 2, "cancelledCount": 0 }
```

ตัวอย่าง error (HTTP 400)

```json
{ "message": "เวลารวมวันที่ 2026-11-18 จะเป็น 510 นาที เกินกำหนด 480 นาที (เหลือได้อีก 210 นาที)" }
```

| HTTP | ความหมาย |
|---|---|
| 200 / 201 | สำเร็จ |
| 400 | ผิดกติกา / ข้อมูลไม่ถูกต้อง (มี `message` ภาษาไทย) |
| 404 | ไม่พบรายการ |

---

## 7. หน้าจอ (Frontend)

- **แท็บบันทึกเวลา**: เลือกวันที่ (ค่าเริ่มต้นวันนี้, ปุ่มเลื่อนวันก่อน/ถัดไป) + progress bar "ใช้ไป X / 480 นาที"
  สีเขียว → เหลือง (≥ 75%) → แดง (≥ 90%)
- ฟอร์มเพิ่ม/แก้ไข: วันที่, โครงการ (dropdown + ปุ่มเพิ่มโครงการใหม่), รายละเอียดงาน, เวลา (นาที) พร้อมปุ่มลัด 15/30/60/120/ที่เหลือ, หมายเหตุ
- รายการของวัน: มือถือแสดงเป็น **card** / จอกว้าง (≥ 768px) แสดงเป็น **table** มีปุ่ม แก้ไข / ยกเลิก / กู้คืน
- รายการที่ยกเลิกแสดงสีจางและขีดฆ่า และไม่นับในยอดรวม
- ข้อความ error จาก API แสดงเป็นกล่องสีแดงเหนือฟอร์ม/ด้านบนหน้า
- **แท็บสรุปตามโครงการ**: เลือกช่วงวันที่ (มีปุ่มลัด สัปดาห์นี้ / เดือนนี้ / 30 วันล่าสุด)

---

## 8. Unit Tests และผลการทดสอบ

รันด้วย `dotnet test` — ใช้ **SQLite in-memory** (สร้างฐานข้อมูลใหม่ทุก test)

**ผลการทดสอบ: ผ่าน 22 / 22 (Failed 0)** — ทดสอบเมื่อ 2026-11-18 บน .NET SDK 8.0.428

| # | กรณีทดสอบ | ผลที่คาดหวัง | ผลลัพธ์ |
|---|---|---|---|
| 1 | เพิ่มรายการให้รวมได้พอดี 480 นาที (200 + 280) | ผ่าน | ✅ Passed |
| 2 | เพิ่มรายการจนรวมได้ 481 นาที (400 + 81) | ไม่ผ่าน + ข้อความ "…จะเป็น 481 นาที เกินกำหนด 480 นาที (เหลือได้อีก 80 นาที)" | ✅ Passed |
| 3 | รายการยกเลิก 300 นาที + รายการปกติ 400 นาที | ผ่าน (ไม่นับรายการยกเลิก, รวม 400) | ✅ Passed |
| 4 | แก้ไขจาก 200 เป็น 280 ในวันที่มีรายการอื่น 200 นาที | ผ่าน (รวม 480 ไม่ใช่ 680) | ✅ Passed |
| 5 | แก้ไขจาก 200 เป็น 281 ในวันที่มีรายการอื่น 200 นาที | ไม่ผ่าน (481) ค่าเดิมไม่เปลี่ยน | ✅ Passed |
| 6 | แก้ไขโดยย้ายไปวันที่ที่เต็มแล้ว (480) | ไม่ผ่าน รายการยังอยู่วันเดิม | ✅ Passed |
| 7 | กู้คืนรายการที่ยกเลิกแล้วทำให้เกิน 480 (300 + 400) | ไม่ผ่าน ยังคงสถานะยกเลิก | ✅ Passed |
| 8 | Minutes = 0, -1, -60 (3 กรณี) | ไม่ผ่าน | ✅ Passed (3/3) |
| 9 | Minutes = 481 ในรายการเดียว | ไม่ผ่าน | ✅ Passed |
| 10 | แก้ไข Minutes เป็น 0 | ไม่ผ่าน | ✅ Passed |
| 11 | แก้ไขย้ายไปวันที่ยังว่าง | ผ่าน วันเดิมลดลง วันใหม่เพิ่มขึ้น | ✅ Passed |
| 12 | กู้คืนรายการที่ไม่ทำให้เกิน 480 | ผ่าน | ✅ Passed |
| 13 | ยกเลิกเป็น soft cancel | ข้อมูลยังอยู่ในฐานข้อมูล | ✅ Passed |
| 14 | แก้ไขรายการที่ยกเลิกแล้ว | ไม่ผ่าน | ✅ Passed |
| 15 | รายละเอียดงานว่าง / ยาวเกิน 500 | ไม่ผ่าน | ✅ Passed |
| 16 | แก้ไข/ยกเลิกรายการที่ไม่มีอยู่ | NotFound (HTTP 404) | ✅ Passed |
| 17 | สรุปรายวันไม่นับรายการยกเลิก | รวม 120, เหลือ 360 | ✅ Passed |
| 18 | สรุปแยกตามโครงการในช่วงวันที่ | จัดกลุ่มถูกต้อง ไม่นับนอกช่วง | ✅ Passed |
| 19 | ชื่อโครงการซ้ำ (ไม่สนตัวพิมพ์/ช่องว่าง) | ไม่ผ่าน | ✅ Passed |
| 20 | ช่วงวันที่กลับด้าน (to < from) | ไม่ผ่าน | ✅ Passed |

ข้อ 1–8 คือกรณีที่โจทย์กำหนด ข้อ 9–20 เป็นกรณีเพิ่มเติม

---

## 9. ข้อมูลตัวอย่าง (Seed data)

| โครงการ | |
|---|---|
| 1 | โครงงานคอมพิวเตอร์ |
| 2 | ฝึกงาน / สหกิจศึกษา |
| 3 | ทบทวนบทเรียนและการบ้าน |

| วันที่ | รายการ | รวม |
|---|---|---|
| 2026-11-17 | 120 + 60 นาที (+ รายการยกเลิก 90 นาที ไม่นับ) | 180 นาที |
| 2026-11-18 | 180 + 90 นาที | 270 นาที |

ดูโครงสร้างตารางได้ที่ [`database/schema.sql`](database/schema.sql) และ [`docs/ER-diagram.md`](docs/ER-diagram.md)
