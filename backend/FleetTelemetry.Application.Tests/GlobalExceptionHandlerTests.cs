using System.Text.Json;
using FleetTelemetry.Api.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetTelemetry.Application.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_returns_problem_details_with_trace_id_for_unhandled_exception()
    {
        var handler = CreateHandler(Environments.Development);
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("fallo de prueba"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        var problem = await DeserializeProblemDetails(httpContext);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("Error interno del servidor", problem.Title);
        Assert.Equal("fallo de prueba", problem.Detail);
        Assert.Equal("/api/test", problem.Instance);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task TryHandleAsync_hides_exception_detail_outside_development()
    {
        var handler = CreateHandler(Environments.Production);
        var httpContext = CreateHttpContext();

        await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("detalle sensible"),
            CancellationToken.None);

        var problem = await DeserializeProblemDetails(httpContext);
        Assert.Equal("Ocurrió un error inesperado. Consulte traceId para soporte.", problem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_maps_argument_exception_to_400()
    {
        var handler = CreateHandler(Environments.Development);
        var httpContext = CreateHttpContext();

        await handler.TryHandleAsync(
            httpContext,
            new ArgumentException("campo inválido"),
            CancellationToken.None);

        var problem = await DeserializeProblemDetails(httpContext);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Solicitud inválida", problem.Title);
        Assert.Equal("campo inválido", problem.Detail);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task TryHandleAsync_maps_dependency_circuit_open_to_503()
    {
        var handler = CreateHandler(Environments.Development);
        var httpContext = CreateHttpContext();

        await handler.TryHandleAsync(
            httpContext,
            new FleetTelemetry.Application.Exceptions.DependencyCircuitOpenException("kafka"),
            CancellationToken.None);

        var problem = await DeserializeProblemDetails(httpContext);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.Equal("Dependencia no disponible", problem.Title);
        Assert.Contains("kafka", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static GlobalExceptionHandler CreateHandler(string environmentName)
    {
        var environment = new HostEnvironment { EnvironmentName = environmentName };
        return new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, environment);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/test";
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "trace-test-123";
        return httpContext;
    }

    private static async Task<ProblemDetailsPayload> DeserializeProblemDetails(HttpContext httpContext)
    {
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var json = await reader.ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetailsPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return problem ?? throw new InvalidOperationException("No se pudo deserializar ProblemDetails.");
    }

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "FleetTelemetry.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ProblemDetailsPayload
    {
        public int? Status { get; set; }
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public string? Instance { get; set; }
        public string? TraceId { get; set; }
    }
}
