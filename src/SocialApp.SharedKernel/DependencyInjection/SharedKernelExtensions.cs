using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using SocialApp.SharedKernel.Errors;
using SocialApp.SharedKernel.Http;

namespace SocialApp.SharedKernel.DependencyInjection;

/// <summary>
/// Ráp toàn bộ hạ tầng dùng chung của SharedKernel: RFC 7807 Problem Details (kèm traceId),
/// exception handler toàn cục, và rate limiting (fixed window). Api chỉ cần gọi
/// <see cref="AddSharedKernel"/> khi cấu hình DI và <see cref="UseSharedKernel"/> trong pipeline.
/// </summary>
public static class SharedKernelExtensions
{
    /// <summary>Tên policy rate limit chặt cho nhóm endpoint xác thực (10 req/phút).</summary>
    public const string AuthRateLimitPolicy = "auth";

    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        // RFC 7807: mọi ProblemDetails đều có traceId (= correlation id), instance và type mặc định.
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
                ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
                ctx.ProblemDetails.Type ??=
                    $"https://httpstatuses.io/{ctx.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError}";
            };
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Mặc định toàn cục: 100 req/phút, phân vùng theo user (nếu đã đăng nhập) hoặc IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Policy chặt cho auth (đăng ký/đăng nhập): 10 req/phút theo IP — chống brute force (ISS-04).
            options.AddPolicy(AuthRateLimitPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    public static IApplicationBuilder UseSharedKernel(this IApplicationBuilder app)
    {
        // Correlation ID chạy sớm nhất để mọi log (kể cả khi xử lý exception) đều có nó.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseRateLimiter();
        return app;
    }

    private static string PartitionKey(HttpContext ctx) =>
        ctx.User.Identity?.IsAuthenticated == true
            ? $"user:{ctx.User.Identity.Name}"
            : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
}
