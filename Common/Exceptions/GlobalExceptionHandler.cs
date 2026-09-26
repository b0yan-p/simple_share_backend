using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace simpleShare_backend.Common.Exceptions;

public class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (exception is AppException)
            logger.LogWarning(exception, "Handled application exception");
        else
            logger.LogError(exception, "Unhandled exception occurred");

        var traceId = httpContext.TraceIdentifier;

        var (status, title, code, errors) = MapException(exception);

        httpContext.Response.StatusCode = status;

        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Type = $"https://httpstatuses.com/{status}"
        };

        // Standard extras for debugging & UI logic
        pd.Extensions["traceId"] = traceId;
        pd.Extensions["code"] = code;

        // Validation field-level errors
        if (errors is not null && errors.Count > 0)
            pd.Extensions["errors"] = errors;

        // Ensure consistent content type
        httpContext.Response.ContentType = "application/problem+json";

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = pd
        });


        // httpContext.Response.StatusCode = exception switch
        // {
        //     ApplicationException => StatusCodes.Status400BadRequest,
        //     _ => StatusCodes.Status500InternalServerError
        // };

        // return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        // {
        //     HttpContext = httpContext,
        //     Exception = exception,
        //     ProblemDetails = new ProblemDetails
        //     {
        //         Type = exception.GetType().Name,
        //         Title = "An error occurred",
        //         Detail = exception.Message
        //     }
        // });
    }

    private static (int status, string title, string code, IDictionary<string, string[]>? errors)
       MapException(Exception ex)
    {
        // 1) Your domain exceptions
        if (ex is ValidationAppException vex)
            return (StatusCodes.Status400BadRequest, "Validation failed", vex.Code, vex.Errors);

        if (ex is UnauthorizedAppException uax)
            return (StatusCodes.Status401Unauthorized, "Unauthorized", uax.Code, null);

        if (ex is NotFoundAppException nfx)
            return (StatusCodes.Status404NotFound, "Not found", nfx.Code, null);

        if (ex is ForbiddenAppException fbx)
            return (StatusCodes.Status403Forbidden, "Forbidden", fbx.Code, null);

        if (ex is ConflictAppException cfx)
            return (StatusCodes.Status409Conflict, "Conflict", cfx.Code, null);

        // 2) DB unique constraint / duplicate key -> 409
        // PostgreSQL: 23505 (unique_violation)
        if (ex is DbUpdateException dbEx &&
            dbEx.InnerException is PostgresException pgEx &&
            pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return (StatusCodes.Status409Conflict, "Conflict", "unique_constraint", null);
        }

        // 3) Fallback
        return (StatusCodes.Status500InternalServerError, "Server error", "server_error", null);
    }
}
