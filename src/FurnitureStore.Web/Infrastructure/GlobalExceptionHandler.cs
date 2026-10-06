using System.Diagnostics;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>
/// Central place that turns exceptions into HTTP responses.
/// - API / AJAX requests: writes the standard ApiResponse JSON envelope.
/// - Browser requests: sets the status code and lets the ExceptionHandler middleware re-execute /Error.
/// Expected (AppException) errors are logged as warnings without stack trace; everything else as errors.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public const string GenericErrorMessage = "Đã có lỗi xảy ra. Vui lòng thử lại sau.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; nothing to send back.
            logger.LogDebug("Request {Method} was cancelled by the client", httpContext.Request.Method);
            return true;
        }

        var (statusCode, message, errors) = Map(exception);
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        // Request.Path already points to the error page here; log the path that actually failed.
        var path = httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? httpContext.Request.Path.Value;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}. TraceId={TraceId}",
                httpContext.Request.Method, path, traceId);
        }
        else
        {
            logger.LogWarning("{ExceptionType} on {Method} {Path}: {Message} TraceId={TraceId}",
                exception.GetType().Name, httpContext.Request.Method, path, exception.Message, traceId);
        }

        httpContext.Response.StatusCode = statusCode;

        if (!httpContext.IsApiRequest())
        {
            // Returning false makes the middleware re-execute ExceptionHandlingPath (/Error) with this status code.
            return false;
        }

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            errors = [$"TraceId: {traceId}"];
        }

        await httpContext.Response.WriteAsJsonAsync(ApiResponse.Fail(message, errors), cancellationToken);
        return true;
    }

    internal static (int StatusCode, string Message, IReadOnlyList<string> Errors) Map(Exception exception) => exception switch
    {
        AppValidationException ex => (StatusCodes.Status400BadRequest, ex.Message, ex.Errors),
        BusinessRuleException ex => (StatusCodes.Status400BadRequest, ex.Message, []),
        DomainException ex => (StatusCodes.Status400BadRequest, ex.Message, []),
        ConflictException ex => (StatusCodes.Status409Conflict, ex.Message, []),
        NotFoundException ex => (StatusCodes.Status404NotFound, ex.Message, []),
        ForbiddenAccessException ex => (StatusCodes.Status403Forbidden, ex.Message, []),
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Vui lòng đăng nhập để tiếp tục.", []),
        BadHttpRequestException ex => (ex.StatusCode, "Yêu cầu không hợp lệ.", []),
        _ => (StatusCodes.Status500InternalServerError, GenericErrorMessage, [])
    };
}
