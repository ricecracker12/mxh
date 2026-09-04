using Microsoft.AspNetCore.Mvc;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.Api.Controllers;

/// <summary>
/// Endpoint mẫu của Walking Skeleton (GĐ0): đi hết pipeline (routing → rate limit → controller →
/// JSON). Kèm 2 endpoint demo để kiểm chứng RFC 7807 (lỗi nghiệp vụ và lỗi không mong muốn).
/// </summary>
[ApiController]
[Route("api/v1/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new PingResponse("pong", HttpContext.TraceIdentifier));

    /// <summary>Demo AppException → Problem Details 409 (có detail + traceId).</summary>
    [HttpGet("app-error")]
    public IActionResult AppError() =>
        throw AppException.Conflict("Đây là lỗi nghiệp vụ mẫu để kiểm chứng RFC 7807.");

    /// <summary>Demo exception chưa xử lý → Problem Details 500 (giấu chi tiết nội bộ).</summary>
    [HttpGet("boom")]
    public IActionResult Boom() =>
        throw new InvalidOperationException("Lỗi nội bộ mẫu — client không nên thấy message này.");
}

public sealed record PingResponse(string Message, string TraceId);
