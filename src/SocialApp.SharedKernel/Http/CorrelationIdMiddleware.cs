using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace SocialApp.SharedKernel.Http;

/// <summary>
/// Gắn correlation ID cho mỗi request: lấy từ header <c>X-Correlation-ID</c> nếu client gửi,
/// nếu không thì sinh mới. Đặt vào <see cref="HttpContext.TraceIdentifier"/> (để traceId trong
/// Problem Details khớp), trả lại header cho client, và đẩy vào Serilog LogContext để mọi log
/// của request đều có CorrelationId.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
                ? incoming.ToString()
                : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
