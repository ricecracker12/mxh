using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// GĐ0 Walking Skeleton: xác nhận pipeline đi hết (routing → middleware → controller → JSON) và
/// error model RFC 7807. Không chạm Postgres/Redis nên an toàn trên CI (chỉ /health/live + /ping).
/// </summary>
public sealed class SmokeEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Health_live_returns_200()
    {
        var response = await Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ping_returns_pong_with_traceId()
    {
        var response = await Client.GetAsync("/api/v1/ping");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PingDto>();
        Assert.Equal("pong", body!.Message);
        Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Unhandled_error_returns_rfc7807_problem_details()
    {
        var response = await Client.GetAsync("/api/v1/ping/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
        Assert.Equal(500, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    private sealed record PingDto(string Message, string TraceId);
    private sealed record ProblemDto(string Title, int Status, string? TraceId);
}
