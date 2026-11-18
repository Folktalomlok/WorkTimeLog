# ระบบบันทึกเวลาปฏิบัติงาน (Work Time Log)

ผู้จัดทำ: **นายกิตติศักดิ์ ชละเอม**

## สำหรับกรรมการ

- 🌐 ระบบที่ใช้งานได้จริง: **<https://worktimelog-nine.vercel.app>** (ใช้ได้ทั้งคอมและมือถือ)
- 💻 โค้ด: [`src/`](src/)
- 🗄️ สคริปต์ฐานข้อมูล: [`database/submission/`](database/submission/) (PostgreSQL และ MySQL)
- 📘 คู่มือการใช้งาน: [`docs/manual/`](docs/manual/) (PDF และ Word)

## ภาพรวม

เว็บแอปให้นักศึกษาฝึกงานบันทึกเวลาที่ใช้กับแต่ละงาน/โครงการในแต่ละวัน เพิ่ม แก้ไข ยกเลิก และกู้คืนรายการได้
แสดงเวลาที่ใช้ไปของวันเป็นแถบสี และสรุปเวลาแยกตามโครงการ มีหน้าแอดมิน (ต้องใช้รหัสผ่าน) สำหรับดูฐานข้อมูล

## ภาพหน้าจอ

| หน้าหลัก | แจ้งเตือนเมื่อเวลารวมเกิน 480 นาที |
|---|---|
| ![หน้าหลัก](docs/manual/images/01-home.png) | ![เกิน 480 นาที](docs/manual/images/03-error-480.png) |

## กติกาของระบบ

1. เวลารวมต่อวันไม่เกิน **480 นาที**
2. แก้ไขรายการแล้วใช้ **ค่าใหม่แทนค่าเดิม** ก่อนรวม (เช่น อื่น 200 + แก้ 200→280 = 480 ไม่ใช่ 680)
3. **ไม่นับรายการที่ยกเลิก** ในเวลารวม (ยกเลิกแบบ soft cancel กู้คืนได้)

## เทคโนโลยี

C# ASP.NET Core 8 · PostgreSQL (Neon) · Bootstrap 5 · xUnit · Vercel

## สถาปัตยกรรม

```mermaid
flowchart LR
    UI["หน้าเว็บ<br/>Bootstrap 5 + JavaScript"] -- "HTTPS / JSON" --> API["ASP.NET Core 8 Web API<br/>Controllers → Services (กติกา 480 นาที)"]
    API -- "EF Core" --> DB[("PostgreSQL<br/>Neon")]
```

## วิธีรันบนเครื่อง

```powershell
winget install Microsoft.DotNet.SDK.8
git clone https://github.com/Folktalomlok/WorkTimeLog.git; cd WorkTimeLog
dotnet run --project src/WorkTimeLog.Api      # เปิด http://localhost:5000 (ไม่ตั้งค่าฐานข้อมูล = ใช้ SQLite)
dotnet test
```

## ผลการทดสอบ

xUnit ผ่าน **40/40** กรณี

---

รายละเอียดสำหรับนักพัฒนา (API, ผลทดสอบทุกกรณี, ตั้งค่า user-secrets, deploy): [docs/DEVELOPER.md](docs/DEVELOPER.md)
