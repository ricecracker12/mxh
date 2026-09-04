using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Formatting.Compact;
using SocialApp.SharedKernel.DependencyInjection;

// Service `migrate` (one-shot, chạy ở bước deploy) gọi với cờ --migrate. GĐ0 chưa có DbContext nên
// đây là no-op thoát 0 — tránh treo container. Từ GĐ1 sẽ apply EF migration tại đây rồi thoát.
if (args.Contains("--migrate"))
{
    Console.WriteLine("[migrate] GĐ0: chưa có migration nào để áp dụng. Thoát 0.");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// --- Logging: Serilog JSON (compact) + correlation id qua LogContext ---
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

// --- Hạ tầng dùng chung: RFC 7807 + exception handler + rate limit ---
builder.Services.AddSharedKernel();

// --- MVC controllers + quy ước JSON (enum dạng chuỗi; field lạ -> 400) ---
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Health checks: /health/ready kiểm tra Postgres + Redis (tag "ready") ---
var postgres = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=socialapp;Username=socialapp;Password=socialapp";
var redis = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

builder.Services.AddHealthChecks()
    .AddNpgSql(postgres, name: "postgres", tags: ["ready"])
    .AddRedis(redis, name: "redis", tags: ["ready"]);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseSharedKernel();

// Swagger bật ở Development + Staging (để demo/test trên staging); TẮT ở Production.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// /health/live: app còn sống (không kiểm phụ thuộc). /health/ready: sẵn sàng nhận tải (DB+Redis OK).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

// Cho phép WebApplicationFactory<Program> trong IntegrationTests tham chiếu Program.
public partial class Program;
