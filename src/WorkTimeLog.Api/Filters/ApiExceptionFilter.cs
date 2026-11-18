using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorkTimeLog.Api.Dtos;
using WorkTimeLog.Api.Services;

namespace WorkTimeLog.Api.Filters;

/// <summary>แปลง exception จาก service เป็น HTTP response พร้อมข้อความภาษาไทย</summary>
public class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case BusinessRuleException ex:
                context.Result = new BadRequestObjectResult(new ErrorResponse(ex.Message));
                context.ExceptionHandled = true;
                break;
            case NotFoundException ex:
                context.Result = new NotFoundObjectResult(new ErrorResponse(ex.Message));
                context.ExceptionHandled = true;
                break;
        }
    }
}
