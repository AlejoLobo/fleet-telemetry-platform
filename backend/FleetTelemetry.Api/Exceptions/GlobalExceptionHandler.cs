using System.Diagnostics;
using FleetTelemetry.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Exceptions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var (statusCode, title, detail) = MapException(exception);

        _logger.LogError(
            exception,
            "Excepción no controlada. TraceId={TraceId} Status={StatusCode}",
            traceId,
            statusCode);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private (int StatusCode, string Title, string Detail) MapException(Exception exception) =>
        exception switch
        {
            ArgumentException argumentException => (
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                argumentException.Message),
            DependencyCircuitOpenException circuitException => (
                StatusCodes.Status503ServiceUnavailable,
                "Dependencia no disponible",
                circuitException.Message),
            UnauthorizedAccessException unauthorizedException => (
                StatusCodes.Status403Forbidden,
                "Acceso denegado",
                unauthorizedException.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Error interno del servidor",
                _environment.IsDevelopment()
                    ? exception.Message
                    : "Ocurrió un error inesperado. Consulte traceId para soporte.")
        };
}
