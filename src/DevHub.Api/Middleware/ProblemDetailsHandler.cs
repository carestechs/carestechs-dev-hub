using System.Diagnostics;
using DevHub.Contracts.ApplicationErrors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Middleware;

/// Translates any uncaught exception into an RFC 7807 problem-details response.
/// <see cref="DomainException"/>s carry their own status/type; everything else maps to 500.
public sealed class ProblemDetailsHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        var (problem, level) = exception switch
        {
            DomainException dx => (BuildFromDomain(dx, httpContext, correlationId), LogLevel.Information),
            _ => (BuildFromUnknown(exception, httpContext, correlationId), LogLevel.Error),
        };

        logger.Log(level, exception,
            "Request {Path} failed with {Status} {Title} (correlationId={CorrelationId})",
            httpContext.Request.Path, problem.Status, problem.Title, correlationId);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails BuildFromDomain(DomainException dx, HttpContext ctx, string correlationId)
    {
        var problem = new ProblemDetails
        {
            Type = dx.Type,
            Title = dx.Title,
            Status = dx.Status,
            Detail = dx.Detail,
            Instance = ctx.Request.Path,
        };
        problem.Extensions["correlationId"] = correlationId;
        if (dx.Errors is not null) problem.Extensions["errors"] = dx.Errors;
        if (dx is ExecutorFailureException ef)
        {
            problem.Extensions["executorKey"] = ef.ExecutorKey;
            problem.Extensions["executorId"] = ef.ExecutorId;
            problem.Extensions["executorCorrelationId"] = ef.CorrelationId;
            if (ef.UpstreamStatus is { } us) problem.Extensions["upstreamStatus"] = us;
            if (ef.UpstreamBody is { Length: > 0 } body) problem.Extensions["details"] = body;
        }
        return problem;
    }

    private static ProblemDetails BuildFromUnknown(Exception ex, HttpContext ctx, string correlationId)
    {
        var problem = new ProblemDetails
        {
            Type = "/probs/internal",
            Title = "Internal Server Error",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "An unexpected error occurred. The incident has been logged.",
            Instance = ctx.Request.Path,
        };
        problem.Extensions["correlationId"] = correlationId;
        return problem;
    }
}
