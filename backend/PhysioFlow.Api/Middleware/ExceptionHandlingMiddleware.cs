using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;

namespace PhysioFlow.Api.Middleware;

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
        try { await _next(context); }
        catch (Exception ex) { await HandleAsync(context, ex); }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        (HttpStatusCode status, object body) = ex switch
        {
            ValidationException v => (HttpStatusCode.BadRequest, new
            {
                title = "Validation failed",
                status = 400,
                code = "validation_failed",
                errorCodes = v.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorCode).ToArray()),
                errors = v.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
            }),
            NotFoundException => (HttpStatusCode.NotFound, new
            {
                title = "Resource not found",
                status = 404,
                detail = ex.Message
            }),
            ForbiddenAccessException f => (HttpStatusCode.Forbidden, new
            {
                title = "Forbidden",
                status = 403,
                code = f.Code,
                detail = ex.Message
            }),
            ConflictException c => (HttpStatusCode.Conflict, new
            {
                title = "Conflict",
                status = 409,
                code = c.Code,
                detail = ex.Message
            }),
            DbUpdateConcurrencyException => (HttpStatusCode.Conflict, new
            {
                title = "Conflict",
                status = 409,
                code = "concurrent_modification",
                detail = "Dane zostały zmienione w międzyczasie. Odśwież widok i spróbuj ponownie."
            }),
            TooManyRequestsException t => (HttpStatusCode.TooManyRequests, new
            {
                title = "Too many requests",
                status = 429,
                code = t.Code,
                detail = ex.Message
            } as object),
            StorageUnavailableException => (HttpStatusCode.ServiceUnavailable, new
            {
                title = "Storage unavailable",
                status = 503,
                code = "storage_unavailable",
                detail = ex.Message
            }),
            EmailDeliveryException => (HttpStatusCode.ServiceUnavailable, new
            {
                title = "Email delivery failed",
                status = 503,
                detail = ex.Message
            }),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, new
            {
                title = "Unauthorized",
                status = 401
            } as object),
            _ => (HttpStatusCode.InternalServerError, new
            {
                title = "Internal server error",
                status = 500,
                detail = "Wystąpił nieoczekiwany błąd."
            })
        };

        if (status == HttpStatusCode.InternalServerError)
            _logger.LogError(ex, "Unhandled exception");
        else
            _logger.LogWarning("Handled exception: {Type} - {Message}", ex.GetType().Name, ex.Message);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }));
    }
}
