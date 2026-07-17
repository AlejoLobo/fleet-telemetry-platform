using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

// FT-005: pendingRecord único, sin Seek/Commit, sin Retry→Successful.
public class FleetRealtimeKafkaPushLoopTests
{
    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay =
        static (_, _) => Task.CompletedTask;

    [Fact]
    public void No_se_consume_offset_51_mientras_50_esta_pendiente()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(50);
        var loop = CreateLoop(broker);

        transport.Enqueue(ConsumeResult(50, ValidVehiclePayload()));
        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));

        var first = loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Equal(KafkaPushPollResult.TransientFailure, first);
        Assert.NotNull(loop.PendingRecord);
        Assert.Equal(50, loop.PendingRecord!.Offset.Value);
        Assert.Equal(1, transport.ConsumeCalls);

        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Equal(1, transport.ConsumeCalls);
        Assert.Equal(50, loop.PendingRecord!.Offset.Value);
        Assert.DoesNotContain(51L, transport.DequeuedOffsets);
    }

    [Fact]
    public void Fallo_transitorio_reintenta_exactamente_el_mismo_ConsumeResult()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(50);
        var loop = CreateLoop(broker);

        var original = ConsumeResult(50, ValidVehiclePayload());
        transport.Enqueue(original);

        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Same(original, loop.PendingRecord);

        loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Same(original, loop.PendingRecord);

        broker.AllowOffset(50);
        var completed = loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Equal(KafkaPushPollResult.Completed, completed);
        Assert.Null(loop.PendingRecord);
        Assert.Equal(50, broker.LastProcessedExternalOffset);
    }

    [Fact]
    public void TransientFailure_no_se_reporta_como_Completed()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FailingBroker(7);
        var loop = CreateLoop(broker);
        transport.Enqueue(ConsumeResult(7, ValidVehiclePayload()));

        var result = loop.RunOnce(transport, TimeSpan.FromSeconds(1));
        Assert.Equal(KafkaPushPollResult.TransientFailure, result);
        Assert.NotEqual(KafkaPushPollResult.Completed, result);
        Assert.NotEqual(KafkaPushPollResult.Idle, result);
    }

    [Fact]
    public void Payload_invalido_completa_y_libera_pending()
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.EstablishBaseline(49);
        var loop = CreateLoop(broker);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        transport.Enqueue(ConsumeResult(50, "{not-json"));
        Assert.Equal(KafkaPushPollResult.Completed, loop.RunOnce(transport, TimeSpan.FromSeconds(1)));
        Assert.Null(loop.PendingRecord);
        Assert.True(broker.IsInvalidCommittedOffset(50));
        Assert.Equal(50, broker.LastProcessedExternalOffset);

        transport.Enqueue(ConsumeResult(51, ValidVehiclePayload()));
        Assert.Equal(KafkaPushPollResult.Completed, loop.RunOnce(transport, TimeSpan.FromSeconds(1)));
        Assert.Equal(51, broker.LastProcessedExternalOffset);

        Assert.True(subscription.LiveReader.TryRead(out var resetEvent));
        Assert.Equal(FleetRealtimeEventTypes.StreamReset, resetEvent.EventType);
    }

    [Fact]
    public void Consume_fatal_devuelve_FatalFailure_sin_absorbido()
    {
        var transport = new FakeKafkaPushTransport
        {
            ThrowOnConsume = () => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_Fatal, "fatal"))
        };
        var loop = CreateLoop(new FleetSseBroker(TimeProvider.System));

        Assert.Equal(KafkaPushPollResult.FatalFailure, loop.RunOnce(transport, TimeSpan.FromMilliseconds(10)));
        Assert.Null(loop.PendingRecord);
    }

    [Fact]
    public void Recuperacion_continua_desde_LastProcessed_mas_uno()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.EstablishBaseline(49);
        for (var offset = 50L; offset <= 55; offset++)
            Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(offset, "alert", new { n = offset }));

        Assert.Equal(55, broker.LastProcessedExternalOffset);
        var resumeOffset = broker.LastProcessedExternalOffset + 1;
        Assert.Equal(56, resumeOffset);

        // Tras recuperación: el siguiente Process acepta desde resume.
        var transport = new FakeKafkaPushTransport();
        var loop = CreateLoop(broker);
        transport.Enqueue(ConsumeResult(56, ValidVehiclePayload()));
        Assert.Equal(KafkaPushPollResult.Completed, loop.RunOnce(transport, TimeSpan.FromSeconds(1)));
        Assert.Equal(56, broker.LastProcessedExternalOffset);
    }

    [Fact]
    public void Perdida_por_retencion_crea_nueva_linea_base()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.EstablishBaseline(10);
        broker.PublishExternal(11, "alert", new { n = 11 });
        broker.SubscribeFrom(new SseLastEventId.Missing());

        var newBaseline = 99L;
        broker.ResetToBaseline(newBaseline);
        Assert.Equal(99, broker.LastProcessedExternalOffset);
        Assert.Empty(broker.SubscribeFrom(new SseLastEventId.ValidCursor(99)).ReplayEvents);

        var missing = broker.SubscribeFrom(new SseLastEventId.Missing());
        Assert.Equal("initial-snapshot", missing.ResetReason);
        Assert.Equal("99", missing.LatestEventId);
    }

    [Fact]
    public void Initial_snapshot_cubre_todo_lo_anterior_al_High_inicial()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        // High=100 → baseline=99
        broker.EstablishBaseline(99);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(99);

        var admission = coordinator.TryOpenStream(new SseLastEventId.Missing());
        Assert.True(admission.Admitted);
        Assert.Equal("initial-snapshot", admission.Subscription!.ResetReason);
        Assert.Equal("99", admission.Subscription.LatestEventId);
        Assert.Equal(99, admission.Subscription.CutoverId);
        Assert.Empty(admission.Subscription.ReplayEvents);
    }

    [Theory]
    [InlineData("value-null")]
    [InlineData("message-null")]
    public void Tombstone_completa_como_invalido_permanente(string tombstoneKind)
    {
        var transport = new FakeKafkaPushTransport();
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.EstablishBaseline(49);
        var loop = CreateLoop(broker);

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
        Assert.Equal(KafkaPushPollResult.Completed, loop.RunOnce(transport, TimeSpan.FromSeconds(1)));
        Assert.True(broker.IsInvalidCommittedOffset(50));
        Assert.Null(loop.PendingRecord);
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
                """{"deviceId":"11111111-1111-1111-1111-111111111111","vehicleName":"VH-001","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"}""").RootElement,
            OccurredAt = DateTimeOffset.UtcNow,
            DeviceId = "11111111-1111-1111-1111-111111111111"
        });

    private sealed class FakeKafkaPushTransport : IRealtimeKafkaPushTransport
    {
        private readonly Queue<ConsumeResult<string, string>> _results = new();

        public int ConsumeCalls { get; private set; }
        public List<long> DequeuedOffsets { get; } = [];
        public Func<ConsumeResult<string, string>?>? ThrowOnConsume { get; set; }

        public void Enqueue(ConsumeResult<string, string> result) => _results.Enqueue(result);

        public ConsumeResult<string, string>? Consume(TimeSpan timeout)
        {
            _ = timeout;
            ConsumeCalls++;
            if (ThrowOnConsume is not null)
                return ThrowOnConsume();

            if (_results.Count == 0)
                return null;

            var next = _results.Dequeue();
            DequeuedOffsets.Add(next.Offset.Value);
            return next;
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
