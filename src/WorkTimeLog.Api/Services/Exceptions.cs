namespace WorkTimeLog.Api.Services;

/// <summary>ผิดกติกาทางธุรกิจ → API ตอบ HTTP 400</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

/// <summary>ไม่พบข้อมูล → API ตอบ HTTP 404</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
