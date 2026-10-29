using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Exceptions;

namespace StockWise.Api.Infrastructure;

/// <summary>Reads the authenticated principal minted by the JWT bearer handler.</summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var sub = Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue("email");
    public string? FullName => Principal?.FindFirstValue("name");
    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? TenantId
    {
        get
        {
            var tid = Principal?.FindFirstValue("tid");
            return Guid.TryParse(tid, out var id) ? id : null;
        }
    }
}

/// <summary>Maps domain/application exceptions to ProblemDetails responses.</summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            await WriteProblemAsync(context, ex.StatusCode, ex.ErrorCode ?? "error", ex.Message, ex.Errors);
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "domain_rule", ex.Message, null);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict on {Method} {Path}; entries: {Entries}",
                context.Request.Method, context.Request.Path,
                string.Join(", ", ex.Entries.Select(e => e.Entity.GetType().Name)));
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, "concurrency",
                "The record was modified by another user. Reload and try again.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "server_error",
                "An unexpected error occurred. The details have been logged.", null);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string errorCode, string detail,
        IDictionary<string, string[]>? errors)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = $"https://httpstatuses.com/{status}",
            title = errorCode,
            status,
            detail,
            errors,
            traceId = context.TraceIdentifier
        };
        await context.Response.WriteAsJsonAsync(problem);
    }
}

public static class CookieNames
{
    public const string RefreshToken = "stockwise_rt";
}
