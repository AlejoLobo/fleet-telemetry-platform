namespace FleetTelemetry.Infrastructure.Realtime;

// Estado de preparación del consumidor Kafka→SSE de la réplica.
public enum FleetKafkaPushReadinessState
{
    Starting = 0,
    Assigned = 1,
    Ready = 2,
    Faulted = 3
}

// Coordina cuándo la réplica puede admitir conexiones SSE.
public interface IFleetKafkaPushReadiness
{
    FleetKafkaPushReadinessState State { get; }

    bool IsReady { get; }

    // Siguiente offset a consumir tras establecer la posición inicial (High watermark).
    long? InitialPositionOffset { get; }

    string? FaultReason { get; }

    void MarkAssigned();

    void EstablishInitialPosition(long nextOffsetToConsume);

    void MarkReady();

    void MarkFaulted(string reason);

    // Polling u otros modos sin consumidor KafkaPush: SSE admisible de inmediato.
    void MarkBypassed();
}

public sealed class FleetKafkaPushReadiness : IFleetKafkaPushReadiness
{
    private readonly object _sync = new();
    private FleetKafkaPushReadinessState _state = FleetKafkaPushReadinessState.Starting;
    private long? _initialPositionOffset;
    private string? _faultReason;
    private bool _positionEstablished;

    public FleetKafkaPushReadinessState State
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
                return _state == FleetKafkaPushReadinessState.Ready;
        }
    }

    public long? InitialPositionOffset
    {
        get
        {
            lock (_sync)
                return _initialPositionOffset;
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

    public void MarkAssigned()
    {
        lock (_sync)
        {
            if (_state is FleetKafkaPushReadinessState.Ready or FleetKafkaPushReadinessState.Faulted)
                return;

            _state = FleetKafkaPushReadinessState.Assigned;
        }
    }

    public void EstablishInitialPosition(long nextOffsetToConsume)
    {
        lock (_sync)
        {
            if (_state == FleetKafkaPushReadinessState.Faulted)
                return;

            if (_state == FleetKafkaPushReadinessState.Starting)
                _state = FleetKafkaPushReadinessState.Assigned;

            _initialPositionOffset = nextOffsetToConsume;
            _positionEstablished = true;
        }
    }

    public void MarkReady()
    {
        lock (_sync)
        {
            if (_state == FleetKafkaPushReadinessState.Faulted)
                return;

            if (!_positionEstablished)
                throw new InvalidOperationException(
                    "Cannot mark Kafka push Ready before establishing the initial consumer position.");

            _state = FleetKafkaPushReadinessState.Ready;
        }
    }

    public void MarkFaulted(string reason)
    {
        lock (_sync)
        {
            _state = FleetKafkaPushReadinessState.Faulted;
            _faultReason = reason;
        }
    }

    public void MarkBypassed()
    {
        lock (_sync)
        {
            _initialPositionOffset = null;
            _positionEstablished = true;
            _faultReason = null;
            _state = FleetKafkaPushReadinessState.Ready;
        }
    }
}
