using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace SocialApp.SharedKernel.Errors;

/// <summary>
/// Bắt mọi exception chưa xử lý và trả về RFC 7807 Problem Details.
/// <see cref="AppException"/> map sang status/title cụ thể; lỗi khác -> 500 (không lộ chi tiết nội bộ).
/// traceId được gắn thống nhất qua CustomizeProblemDetails (xem AddSharedKernel).
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            AppException ae => (ae.Status, ae.Title),
            _ => (StatusCodes.Status500InternalServerError, "Đã xảy ra lỗi không mong muốn")
        };

        // 5xx là bất thường -> log Error; 4xx là lỗi nghiệp vụ mong đợi -> log Warning.
        if (status >= 500)
            logger.LogError(exception, "Unhandled exception ({Status})", status);
        else
            logger.LogWarning(exception, "Handled app exception ({Status}): {Title}", status, title);

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Chỉ lộ detail cho lỗi nghiệp vụ đã biết; lỗi 500 giấu chi tiết.
            Detail = exception is AppException ? exception.Message : null,
        };

        if (exception is AppException { Errors: { } errors })
            problemDetails.Extensions["errors"] = errors;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });
    }
}
