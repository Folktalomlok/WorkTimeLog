namespace WorkTimeLog.Api.Models;

/// <summary>
/// บันทึกการลองรหัสผ่านแอดมินที่ผิด ใช้จำกัด 5 ครั้งต่อ 15 นาทีต่อ IP
/// เก็บในฐานข้อมูล (ไม่ใช่หน่วยความจำ) เพราะบน Vercel มีหลาย instance และ instance ถูกปิด/เปิดใหม่ได้ตลอด
/// </summary>
public class AdminLoginAttempt
{
    public long Id { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public DateTime AttemptedAt { get; set; }
}
