using System.Text;
using DevHub.Api;
using DevHub.Api.Middleware;
using DevHub.Api.Options;
using DevHub.Contracts.Persistence;
using DevHub.Modules.Audit;
using DevHub.Modules.ExecutorRegistry;
using DevHub.Modules.Identity;
using DevHub.Modules.Notifications;
using DevHub.Modules.WorkItems;
using DevHub.Modules.Workspace;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------------------
// Strongly-typed, validated options.
// ----------------------------------------------------------------------------
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<OperatorSeedOptions>()
    .Bind(builder.Configuration.GetSection(OperatorSeedOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ----------------------------------------------------------------------------
// Cross-cutting infrastructure: timestamping interceptor used by every module's
// DbContext (registered as a singleton; safe — it carries no state).
// ----------------------------------------------------------------------------
builder.Services.AddSingleton<TimestampingInterceptor>();

// ----------------------------------------------------------------------------
// AuthN — JWT bearer. AuthZ — default scheme; per-endpoint policies land later.
// ----------------------------------------------------------------------------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// ----------------------------------------------------------------------------
// CORS — single SPA origin, with credentials (for the refresh cookie).
// ----------------------------------------------------------------------------
var corsOpts = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
               ?? throw new InvalidOperationException("Cors configuration section is missing.");
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOpts.SpaOrigin)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// ----------------------------------------------------------------------------
// RFC 7807 problem details — uniform error contract across modules.
// ----------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsHandler>();

builder.Services.AddHttpContextAccessor();
// Controllers live in feature modules. Each module's assembly is registered as
// an application part so MVC discovers its [ApiController] types. Host owns
// no controllers itself.
builder.Services.AddControllers()
    .AddApplicationPart(typeof(DevHub.Modules.Workspace.WorkspaceDbContext).Assembly)
    .AddApplicationPart(typeof(DevHub.Modules.Identity.IdentityDbContext).Assembly)
    .AddApplicationPart(typeof(DevHub.Modules.ExecutorRegistry.ExecutorRegistryDbContext).Assembly)
    .AddApplicationPart(typeof(DevHub.Modules.WorkItems.WorkItemsDbContext).Assembly)
    .AddApplicationPart(typeof(DevHub.Modules.Audit.AuditDbContext).Assembly)
    .AddApplicationPart(typeof(DevHub.Modules.Notifications.NotificationsDbContext).Assembly);

// ----------------------------------------------------------------------------
// Module registration. Each module is self-contained: it owns its DbContext,
// its controllers, and its services. Order is independent.
// ----------------------------------------------------------------------------
builder.Services
    .AddWorkspaceModule(builder.Configuration)
    .AddIdentityModule(builder.Configuration)
    .AddExecutorRegistryModule(builder.Configuration)
    .AddWorkItemsModule(builder.Configuration)
    .AddAuditModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<WorkspaceDbContext>(name: "db", failureStatus: HealthStatus.Unhealthy);

var app = builder.Build();

// ----------------------------------------------------------------------------
// Pipeline. ExceptionHandler is early so it catches everything downstream.
// ----------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
}).AllowAnonymous();

app.Run();

// Exposes Program for WebApplicationFactory in test projects (see plan-T-020).
public partial class Program;
