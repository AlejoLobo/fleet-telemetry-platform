using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Realtime;

namespace FleetTelemetry.Infrastructure.Realtime;

// Estado del stream SSE KafkaPush de la réplica (sin consumer-group rebalance).
public enum RealtimeStreamState
{
    Starting = 0,
    Ready = 1,
    Recovering = 2,
    Faulted = 3
}

// Resultado atómico de admisión SSE.
public sealed record StreamAdmissionResult(
    bool Admitted,
    SseSubscription? Subscription,
    RealtimeStreamState State,
    string? Reason);

// Único propietario del estado de admisión SSE y del epoch de generación.
public interface IRealtimeStreamCoordinator
{
    RealtimeStreamState State { get; }

    bool IsReady { get; }

    long? BaselineOffset { get; }

    string? FaultReason { get; }

    long Epoch { get; }

    // Comprueba Ready y crea la suscripción bajo el mismo lock.
    StreamAdmissionResult TryOpenStream(SseLastEventId cursor);

    void EnterReady(long? baselineOffset = null);

    void EnterRecovering(string reason);

    void EnterFaulted(string reason);

    // Modo Polling: admite SSE sin consumidor KafkaPush.
    void MarkBypassed();
}

public sealed class RealtimeStreamCoordinator : IRealtimeStreamCoordinator
{
    private readonly FleetSseBroker _broker;
    private readonly object _sync = new();
    private RealtimeStreamState _state = RealtimeStreamState.Starting;
    private long? _baselineOffset;
    private string? _faultReason;
    private long _epoch;

    public RealtimeStreamCoordinator(FleetSseBroker broker)
    {
        _broker = broker;
    }

    public RealtimeStreamState State
    {
        get
        {
            lock (_sync)
                return _state;
        }
    }

    public bool IsReady
    {
        get
        {
            lock (_sync)
                return _state == RealtimeStreamState.Ready;
        }
    }

    public long? BaselineOffset
    {
        get
        {
            lock (_sync)
                return _baselineOffset;
        }
    }

    public string? FaultReason
    {
        get
        {
            lock (_sync)
                return _faultReason;
        }
    }

    public long Epoch
    {
        get
        {
            lock (_sync)
                return _epoch;
        }
    }

    public StreamAdmissionResult TryOpenStream(SseLastEventId cursor)
    {
        lock (_sync)
        {
            if (_state != RealtimeStreamState.Ready)
            {
                return new StreamAdmissionResult(
                    Admitted: false,
                    Subscription: null,
                    State: _state,
                    Reason: "kafka-push-not-ready");
            }

            var subscription = _broker.SubscribeFrom(cursor);
            return new StreamAdmissionResult(
                Admitted: true,
                Subscription: subscription,
                State: _state,
                Reason: null);
        }
    }

    public void EnterReady(long? baselineOffset = null)
    {
        lock (_sync)
        {
            if (_state == RealtimeStreamState.Faulted)
                return;

            if (baselineOffset.HasValue)
                _baselineOffset = baselineOffset;

            _faultReason = null;
            _state = RealtimeStreamState.Ready;
        }
    }

    public void EnterRecovering(string reason)
    {
        lock (_sync)
        {
            if (_state == RealtimeStreamState.Faulted)
                return;

            _state = RealtimeStreamState.Recovering;
            _faultReason = reason;
            _epoch++;
            _broker.CompleteAllSubscribers(reason);
        }
    }

    public void EnterFaulted(string reason)
    {
        lock (_sync)
        {
            _state = RealtimeStreamState.Faulted;
            _faultReason = reason;
            _epoch++;
            _broker.CompleteAllSubscribers(reason);
        }
    }

    public void MarkBypassed()
    {
        lock (_sync)
        {
            _baselineOffset = null;
            _faultReason = null;
            _state = RealtimeStreamState.Ready;
        }
    }
}
