using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

public class FleetSseBrokerWatermarkTests
{
    private static SseLastEventId Valid(long value) => new SseLastEventId.ValidCursor(value);

    [Fact]
    public void Buffer_con_hueco_invalido_genera_invalid_payload_gap()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 20);
        broker.PublishExternal(49, "alert", new { n = 49 });
        Assert.Equal(ExternalPublishResult.Accepted, broker.RecordInvalidExternalOffset(50));
        broker.PublishExternal(51, "alert", new { n = 51 });

        Assert.Equal(51, broker.LastProcessedExternalOffset);

        var subscription = broker.SubscribeFrom(Valid(49));
        Assert.Equal(SseReplayStatus.ReplayGap, subscription.ReplayStatus);
        Assert.Equal("invalid-payload-gap", subscription.ResetReason);
        Assert.Equal(51, subscription.CutoverId);
        Assert.Equal("51", subscription.LatestEventId);
    }

    [Fact]
    public void Primer_mensaje_invalido_expone_latestEventId_exacto()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        Assert.Equal(ExternalPublishResult.Accepted, broker.RecordInvalidExternalOffset(50));

        Assert.Equal(50, broker.LastProcessedExternalOffset);
        broker.PublishStreamResetToAll("invalid-payload");

        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());
        Assert.Equal(50, subscription.CutoverId);
        Assert.Equal("50", subscription.LatestEventId);
    }

    [Fact]
    public void Cursor_igual_al_ultimo_offset_invalido_queda_al_dia()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.RecordInvalidExternalOffset(50);

        var subscription = broker.SubscribeFrom(Valid(50));
        Assert.Equal(SseReplayStatus.ReplayAvailable, subscription.ReplayStatus);
        Assert.Null(subscription.ResetReason);
        Assert.Empty(subscription.ReplayEvents);
        Assert.Equal(50, subscription.CutoverId);
    }

    [Fact]
    public void Consulta_con_distancias_grandes_de_offsets_termina_acotada()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 200);
        broker.PublishExternal(100, "alert", new { n = 1 });
        broker.RecordInvalidExternalOffset(101);
        broker.PublishExternal(250, "alert", new { n = 2 });

        var started = DateTime.UtcNow;
        var subscription = broker.SubscribeFrom(Valid(100));
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(SseReplayStatus.ReplayGap, subscription.ReplayStatus);
        Assert.Equal("invalid-payload-gap", subscription.ResetReason);
    }

    [Fact]
    public void Retencion_de_offsets_invalidos_permanece_acotada()
    {
        var broker = new FleetSseBroker(
            TimeProvider.System,
            replayBufferSize: 10,
            maxInvalidOffsetsCovered: 20);

        for (var i = 0; i < 100; i++)
            broker.RecordInvalidExternalOffset(i);

        Assert.True(broker.InvalidOffsetCoveredCount <= 20);
        Assert.Equal(99, broker.LastProcessedExternalOffset);
    }
}
