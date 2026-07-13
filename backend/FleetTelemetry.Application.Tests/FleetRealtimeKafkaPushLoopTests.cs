using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

// FT-005: loop Kafka→SSE con confirmación secuencial de offsets.
public class FleetRealtimeKafkaPushLoopTests
{
    [Fact]
    public void Fallo_del_broker_no_confirma_offset_actual()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(50);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));

        transport.Enqueue(ConsumeResult(50, ValidPayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Empty(transport.CommittedOffsets);
        Assert.Equal(50, loop.BlockedOffset);
        Assert.Single(transport.SeekCalls);
        Assert.Equal(50, transport.SeekCalls[0]);
    }

    [Fact]
    public void Fallo_del_offset_50_impide_confirmar_51()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(50);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));

        transport.Enqueue(ConsumeResult(50, ValidPayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        transport.Enqueue(ConsumeResult(51, ValidPayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Empty(transport.CommittedOffsets);
        Assert.Equal(50, loop.BlockedOffset);
        Assert.Contains(50, transport.SeekCalls);
    }

    [Fact]
    public void Reintento_exitoso_confirma_una_sola_vez()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(50);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));

        transport.Enqueue(ConsumeResult(50, ValidPayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        broker.AllowOffset(50);
        transport.Enqueue(ConsumeResult(50, ValidPayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50], transport.CommittedOffsets);
        Assert.Null(loop.BlockedOffset);
    }

    [Fact]
    public void Payload_invalido_no_crea_hueco_silencioso_en_el_stream()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FleetSseBroker(TimeProvider.System);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        transport.Enqueue(ConsumeResult(50, "{not-json"));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        transport.Enqueue(ConsumeResult(51, ValidPayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Empty(transport.CommittedOffsets);
        Assert.Equal(50, loop.BlockedOffset);
        Assert.False(subscription.LiveReader.TryRead(out _));

        transport.Enqueue(ConsumeResult(50, ValidPayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50], transport.CommittedOffsets);
        Assert.True(subscription.LiveReader.TryRead(out var evt));
        Assert.Equal(50, evt.StreamId);
    }

    private static ConsumeResult<string, string> ConsumeResult(long offset, string payload) =>
        new()
        {
            Topic = "fleet.realtime",
            Partition = 0,
            Offset = offset,
            Message = new Message<string, string> { Value = payload }
        };

    private static string ValidPayload() =>
        FleetRealtimeKafkaMessage.Serialize(new FleetRealtimeKafkaMessage
        {
            EventType = FleetRealtimeEventTypes.VehicleUpdate,
            Payload = System.Text.Json.JsonDocument.Parse("""{"vehicleId":"VH-001","status":"online"}""").RootElement,
            OccurredAt = DateTimeOffset.UtcNow,
            VehicleId = "VH-001"
        });

    private sealed class FakeKafkaPushTransport : IRealtimeKafkaPushTransport
    {
        private readonly Queue<ConsumeResult<string, string>> _results = new();

        public List<long> CommittedOffsets { get; } = [];
        public List<long> SeekCalls { get; } = [];

        public void Enqueue(ConsumeResult<string, string> result) => _results.Enqueue(result);

        public ConsumeResult<string, string>? Consume(TimeSpan timeout)
        {
            _ = timeout;
            return _results.Count == 0 ? null : _results.Dequeue();
        }

        public void Commit(ConsumeResult<string, string> result) =>
            CommittedOffsets.Add(result.Offset.Value);

        public void Seek(long offset) => SeekCalls.Add(offset);
    }

    private sealed class FailingBroker : FleetSseBroker
    {
        private readonly HashSet<long> _denied = [];

        public FailingBroker(long deniedOffset) : base(TimeProvider.System) =>
            _denied.Add(deniedOffset);

        public void AllowOffset(long offset) => _denied.Remove(offset);

        public override ExternalPublishResult PublishExternal(
            long streamId,
            string eventType,
            object data,
            DateTimeOffset? timestamp = null)
        {
            if (_denied.Contains(streamId))
                throw new InvalidOperationException($"Simulated publish failure at offset {streamId}");

            return base.PublishExternal(streamId, eventType, data, timestamp);
        }
    }
}
