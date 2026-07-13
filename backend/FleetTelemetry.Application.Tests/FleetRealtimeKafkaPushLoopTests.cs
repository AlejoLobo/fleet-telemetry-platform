using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

// FT-005: loop Kafka→SSE con confirmación secuencial de offsets.
public class FleetRealtimeKafkaPushLoopTests
{
    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay =
        static (_, _) => Task.CompletedTask;

    [Fact]
    public void Fallo_del_broker_no_confirma_offset_actual()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(50);
        var loop = CreateLoop(broker);

        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Empty(transport.CommittedOffsets);
        Assert.Equal(50, loop.BlockedOffset);
        Assert.Single(transport.SeekCalls);
        Assert.Equal(50, transport.SeekCalls[0]);
    }

    [Fact]
    public void Fallo_transitorio_del_offset_50_impide_confirmar_51()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(50);
        var loop = CreateLoop(broker);

        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));
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
        var loop = CreateLoop(broker);

        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        broker.AllowOffset(50);
        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50], transport.CommittedOffsets);
        Assert.Null(loop.BlockedOffset);
    }

    [Fact]
    public void Payload_invalido_inmutable_no_bloquea_offset_51()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FleetSseBroker(TimeProvider.System);
        var loop = CreateLoop(broker);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        transport.Enqueue(ConsumeResult(50, "{not-json"));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50], transport.CommittedOffsets);
        Assert.Null(loop.BlockedOffset);
        Assert.True(broker.IsInvalidCommittedOffset(50));
        Assert.Equal(50, broker.LastAcceptedExternalOffset);

        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50, 51], transport.CommittedOffsets);
        Assert.Null(loop.BlockedOffset);

        Assert.True(subscription.LiveReader.TryRead(out var resetEvent));
        Assert.Equal(FleetRealtimeEventTypes.StreamReset, resetEvent.EventType);

        Assert.True(subscription.LiveReader.TryRead(out var liveEvent));
        Assert.Equal(51, liveEvent.StreamId);

        var reconnect = broker.SubscribeFrom(new SseLastEventId.ValidCursor(49));
        Assert.Equal(SseReplayStatus.ReplayGap, reconnect.ReplayStatus);
        Assert.Equal("invalid-payload-gap", reconnect.ResetReason);
    }

    [Theory]
    [InlineData("value-null")]
    [InlineData("message-null")]
    public void Tombstone_se_trata_como_payload_invalido(string tombstoneKind)
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FleetSseBroker(TimeProvider.System);
        var loop = CreateLoop(broker);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        var tombstone = new ConsumeResult<string, string>
        {
            Topic = "fleet.realtime",
            Partition = 0,
            Offset = 50,
            Message = tombstoneKind == "message-null"
                ? null!
                : new Message<string, string> { Value = null! }
        };
        transport.Enqueue(tombstone);
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50], transport.CommittedOffsets);
        Assert.True(broker.IsInvalidCommittedOffset(50));
        Assert.True(subscription.LiveReader.TryRead(out var resetEvent));
        Assert.Equal(FleetRealtimeEventTypes.StreamReset, resetEvent.EventType);

        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50, 51], transport.CommittedOffsets);
        Assert.True(subscription.LiveReader.TryRead(out var liveEvent));
        Assert.Equal(51, liveEvent.StreamId);

        var reconnect = broker.SubscribeFrom(new SseLastEventId.ValidCursor(49));
        Assert.Equal(SseReplayStatus.ReplayGap, reconnect.ReplayStatus);
        Assert.Equal("invalid-payload-gap", reconnect.ResetReason);
    }

    [Fact]
    public void Commit_fallido_bloquea_offset_y_acepta_Duplicate_en_reintento()
    {
        var transport = new FakeKafkaPushTransport { FailCommitTimes = 1 };
        var broker = new FleetSseBroker(TimeProvider.System);
        var loop = CreateLoop(broker);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Empty(transport.CommittedOffsets);
        Assert.Equal(50, loop.BlockedOffset);
        Assert.True(subscription.LiveReader.TryRead(out var first));
        Assert.Equal(50, first.StreamId);
        Assert.False(subscription.LiveReader.TryRead(out _));

        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Empty(transport.CommittedOffsets);
        Assert.Equal(50, loop.BlockedOffset);
        Assert.False(subscription.LiveReader.TryRead(out _));

        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([50], transport.CommittedOffsets);
        Assert.Null(loop.BlockedOffset);
        Assert.False(subscription.LiveReader.TryRead(out _));

        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Equal([50, 51], transport.CommittedOffsets);
        Assert.True(subscription.LiveReader.TryRead(out var second));
        Assert.Equal(51, second.StreamId);
    }

    [Fact]
    public void Seek_transitorio_mantiene_bloqueado_sin_avanzar()
    {
        var transport = new FakeKafkaPushTransport { FailSeekTimes = 1 };
        var broker = new FailingBroker(50);
        var loop = CreateLoop(broker);

        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Equal(50, loop.BlockedOffset);

        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Empty(transport.CommittedOffsets);
        Assert.Equal(50, loop.BlockedOffset);

        broker.AllowOffset(50);
        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Equal([50], transport.CommittedOffsets);
        Assert.Null(loop.BlockedOffset);
    }

    [Theory]
    [InlineData("future-schema", """{"schemaVersion":99,"eventType":"vehicle-update","payload":{"vehicleId":"VH-001","name":"VH-001","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"},"occurredAt":"2026-07-13T10:00:00Z"}""")]
    [InlineData("missing-schema", """{"eventType":"vehicle-update","payload":{"vehicleId":"VH-001","name":"VH-001","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"},"occurredAt":"2026-07-13T10:00:00Z"}""")]
    [InlineData("unknown-event", """{"schemaVersion":1,"eventType":"unknown-event","payload":{"vehicleId":"VH-001"},"occurredAt":"2026-07-13T10:00:00Z"}""")]
    [InlineData("null-payload", """{"schemaVersion":1,"eventType":"vehicle-update","payload":null,"occurredAt":"2026-07-13T10:00:00Z"}""")]
    [InlineData("missing-status", """{"schemaVersion":1,"eventType":"vehicle-update","payload":{"vehicleId":"VH-001","name":"VH-001","lastSeenAt":"2026-07-13T10:00:00Z"},"occurredAt":"2026-07-13T10:00:00Z"}""")]
    [InlineData("alert-missing-alertId", """{"schemaVersion":1,"eventType":"alert","payload":{"vehicleId":"VH-001","alertType":"speed","severity":"high","message":"x","createdAt":"2026-07-13T10:00:00Z","isAcknowledged":false},"occurredAt":"2026-07-13T10:00:00Z"}""")]
    [InlineData("alert-missing-createdAt", """{"schemaVersion":1,"eventType":"alert","payload":{"alertId":"11111111-1111-1111-1111-111111111111","vehicleId":"VH-001","alertType":"speed","severity":"high","message":"x","isAcknowledged":false},"occurredAt":"2026-07-13T10:00:00Z"}""")]
    [InlineData("fleet-empty-element", """{"schemaVersion":1,"eventType":"fleet-update","payload":[{}],"occurredAt":"2026-07-13T10:00:00Z"}""")]
    public void Contrato_invalido_avanza_offset_y_fuerza_reset(string _case, string invalidJson)
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FleetSseBroker(TimeProvider.System);
        var loop = CreateLoop(broker);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        transport.Enqueue(ConsumeResult(70, invalidJson));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        transport.Enqueue(ConsumeResult(71, ValidVehiclePayload()));
        loop.RunOnce(transport, TimeSpan.FromSeconds(1));

        Assert.Equal([70, 71], transport.CommittedOffsets);
        Assert.True(broker.IsInvalidCommittedOffset(70));
        Assert.Equal(71, broker.LastProcessedExternalOffset);

        Assert.True(subscription.LiveReader.TryRead(out var resetEvent));
        Assert.Equal(FleetRealtimeEventTypes.StreamReset, resetEvent.EventType);
        Assert.True(subscription.LiveReader.TryRead(out var liveEvent));
        Assert.Equal(71, liveEvent.StreamId);
        _ = _case;
    }

    private static FleetRealtimeKafkaPushLoop CreateLoop(FleetSseBroker broker) =>
        new(new RealtimeKafkaPushProcessor(broker), delayAsync: NoDelay, backoff: TimeSpan.Zero);

    private static ConsumeResult<string, string> ConsumeResult(long offset, string payload) =>
        new()
        {
            Topic = "fleet.realtime",
            Partition = 0,
            Offset = offset,
            Message = new Message<string, string> { Value = payload }
        };

    private static string ValidVehiclePayload() =>
        FleetRealtimeKafkaMessage.Serialize(new FleetRealtimeKafkaMessage
        {
            SchemaVersion = FleetRealtimeKafkaMessage.CurrentSchemaVersion,
            EventType = FleetRealtimeEventTypes.VehicleUpdate,
            Payload = System.Text.Json.JsonDocument.Parse(
                """{"vehicleId":"VH-001","name":"VH-001","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"}""").RootElement,
            OccurredAt = DateTimeOffset.UtcNow,
            VehicleId = "VH-001"
        });

    private sealed class FakeKafkaPushTransport : IRealtimeKafkaPushTransport
    {
        private readonly Queue<ConsumeResult<string, string>> _results = new();

        public List<long> CommittedOffsets { get; } = [];
        public List<long> SeekCalls { get; } = [];
        public int FailCommitTimes { get; set; }
        public int FailSeekTimes { get; set; }

        public void Enqueue(ConsumeResult<string, string> result) => _results.Enqueue(result);

        public ConsumeResult<string, string>? Consume(TimeSpan timeout)
        {
            _ = timeout;
            return _results.Count == 0 ? null : _results.Dequeue();
        }

        public void Commit(ConsumeResult<string, string> result)
        {
            if (FailCommitTimes > 0)
            {
                FailCommitTimes--;
                throw new KafkaException(new Error(ErrorCode.Local_Fail, "commit failed"));
            }

            CommittedOffsets.Add(result.Offset.Value);
        }

        public void Seek(long offset)
        {
            if (FailSeekTimes > 0)
            {
                FailSeekTimes--;
                throw new KafkaException(new Error(ErrorCode.Local_Fail, "seek failed"));
            }

            SeekCalls.Add(offset);
        }
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
            {
                throw new RealtimeKafkaTransientPublishException(
                    $"Simulated publish failure at offset {streamId}.",
                    new InvalidOperationException("broker down"));
            }

            return base.PublishExternal(streamId, eventType, data, timestamp);
        }
    }
}
