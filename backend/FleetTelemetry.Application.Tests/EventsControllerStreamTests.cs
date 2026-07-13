using System.Text;
using FleetTelemetry.Api.Controllers;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

// FT-005: connected efímero solo en la conexión HTTP que lo solicita.
public class EventsControllerStreamTests
{
    [Fact]
    public async Task Connected_solo_se_escribe_en_la_conexion_HTTP_nueva()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var controller = CreateController(broker);

        var (firstContext, firstCts) = CreateHttpContext();
        var (secondContext, secondCts) = CreateHttpContext();

        controller.ControllerContext = new ControllerContext { HttpContext = firstContext };
        var firstTask = controller.Stream(CancellationToken.None);

        controller.ControllerContext = new ControllerContext { HttpContext = secondContext };
        var secondTask = controller.Stream(CancellationToken.None);

        await Task.Delay(100);

        firstCts.Cancel();
        secondCts.Cancel();

        await Task.WhenAll(
            Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstTask),
            Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask));

        var firstText = await ReadResponseBodyAsync(firstContext);
        var secondText = await ReadResponseBodyAsync(secondContext);

        Assert.Equal(1, CountOccurrences(firstText, "event: connected"));
        Assert.Equal(1, CountOccurrences(secondText, "event: connected"));
        Assert.Contains("initial-snapshot", firstText);
        Assert.Contains("initial-snapshot", secondText);
    }

    [Fact]
    public async Task Dos_conexiones_HTTP_existentes_no_reciben_el_connected_de_una_tercera()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var controller = CreateController(broker);

        var (firstContext, firstCts) = CreateHttpContext();
        var (secondContext, secondCts) = CreateHttpContext();
        var (thirdContext, thirdCts) = CreateHttpContext();

        controller.ControllerContext = new ControllerContext { HttpContext = firstContext };
        var firstTask = controller.Stream(CancellationToken.None);

        controller.ControllerContext = new ControllerContext { HttpContext = secondContext };
        var secondTask = controller.Stream(CancellationToken.None);

        await Task.Delay(100);

        var firstBefore = await ReadResponseBodyAsync(firstContext);
        var secondBefore = await ReadResponseBodyAsync(secondContext);

        controller.ControllerContext = new ControllerContext { HttpContext = thirdContext };
        var thirdTask = controller.Stream(CancellationToken.None);

        await Task.Delay(100);

        firstCts.Cancel();
        secondCts.Cancel();
        thirdCts.Cancel();

        await Task.WhenAll(
            Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstTask),
            Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask),
            Assert.ThrowsAnyAsync<OperationCanceledException>(() => thirdTask));

        var firstAfter = await ReadResponseBodyAsync(firstContext);
        var secondAfter = await ReadResponseBodyAsync(secondContext);
        var thirdText = await ReadResponseBodyAsync(thirdContext);

        Assert.Equal(firstBefore, firstAfter);
        Assert.Equal(secondBefore, secondAfter);
        Assert.Equal(1, CountOccurrences(thirdText, "event: connected"));
    }

    [Fact]
    public async Task Stream_responde_503_si_kafka_push_no_esta_Ready()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        Assert.Equal(FleetKafkaPushReadinessState.Starting, readiness.State);

        var controller = CreateController(broker, readiness);
        var (context, _) = CreateHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        await controller.Stream(CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        var body = await ReadResponseBodyAsync(context);
        Assert.Contains("kafka-push-not-ready", body);
        Assert.Contains("Starting", body);
    }

    [Fact]
    public async Task SSE_responde_503_durante_rebalance()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        readiness.EstablishFirstAssignmentPosition(100);
        readiness.MarkReady();
        readiness.MarkRebalancing();

        var controller = CreateController(broker, readiness);
        var (context, _) = CreateHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        await controller.Stream(CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        var body = await ReadResponseBodyAsync(context);
        Assert.Contains("kafka-push-not-ready", body);
        Assert.Contains("Rebalancing", body);
    }

    [Fact]
    public async Task Reconexion_durante_Faulted_recibe_503()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        readiness.EstablishFirstAssignmentPosition(100);
        readiness.MarkReady();

        // Particiones perdidas → EnterFaulted (cierra SSE y deja readiness Faulted).
        coordinator.HandlePartitionsLost([]);
        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);

        var controller = CreateController(broker, readiness);
        var (context, _) = CreateHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        await controller.Stream(CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        var body = await ReadResponseBodyAsync(context);
        Assert.Contains("kafka-push-not-ready", body);
        Assert.Contains("Faulted", body);
    }

    private static EventsController CreateController(
        FleetSseBroker broker,
        IFleetKafkaPushReadiness? readiness = null)
    {
        readiness ??= CreateReadyReadiness();
        return new(
            broker,
            Options.Create(new SseOptions
            {
                Mode = SseDeliveryMode.KafkaPush,
                InstanceId = "api-test"
            }),
            readiness);
    }

    private static IFleetKafkaPushReadiness CreateReadyReadiness()
    {
        var readiness = new FleetKafkaPushReadiness();
        readiness.EstablishInitialPosition(0);
        readiness.MarkReady();
        return readiness;
    }

    private static (DefaultHttpContext Context, CancellationTokenSource Cts) CreateHttpContext()
    {
        var cts = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestAborted = cts.Token;
        return (context, cts);
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
