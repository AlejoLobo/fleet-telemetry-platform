namespace FleetTelemetry.Infrastructure.Realtime;

// Estado de preparación del consumidor Kafka→SSE de la réplica.
public enum FleetKafkaPushReadinessState
{
    Starting = 0,
    Assigned = 1,
    Ready = 2,
    Rebalancing = 3,
    Faulted = 4
}

// Coordina cuándo la réplica puede admitir conexiones SSE.
public interface IFleetKafkaPushReadiness
{
    FleetKafkaPushReadinessState State { get; }

    bool IsReady { get; }

    // Primera posición High del proceso (línea base para initial-snapshot).
    long? InitialPositionOffset { get; }

    // Offset de reanudación de la asignación actual.
    long? CurrentResumeOffset { get; }

    bool HasCompletedFirstAssignment { get; }

    string? FaultReason { get; }

    void MarkRebalancing();

    void MarkAssigned();

    // Primera asignación: fija InitialPositionOffset (= High) y resume.
    void EstablishFirstAssignmentPosition(long nextOffsetToConsume);

    // Reasignación: resume sin consultar un nuevo High.
    void EstablishResumePosition(long nextOffsetToConsume);

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
    private long? _currentResumeOffset;
    private string? _faultReason;
    private bool _assignmentPositionEstablished;
    private bool _hasCompletedFirstAssignment;

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

    public long? CurrentResumeOffset
    {
        get
        {
            lock (_sync)
                return _currentResumeOffset;
        }
    }

    public bool HasCompletedFirstAssignment
    {
        get
        {
            lock (_sync)
                return _hasCompletedFirstAssignment;
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

    public void MarkRebalancing()
    {
        lock (_sync)
        {
            if (_state == FleetKafkaPushReadinessState.Faulted)
                return;

            _state = FleetKafkaPushReadinessState.Rebalancing;
            _assignmentPositionEstablished = false;
            _currentResumeOffset = null;
        }
    }

    public void MarkAssigned()
    {
        lock (_sync)
        {
            if (_state == FleetKafkaPushReadinessState.Faulted)
                return;

            _state = FleetKafkaPushReadinessState.Assigned;
            _assignmentPositionEstablished = false;
            _currentResumeOffset = null;
        }
    }

    public void EstablishFirstAssignmentPosition(long nextOffsetToConsume)
    {
        lock (_sync)
        {
            if (_state == FleetKafkaPushReadinessState.Faulted)
                return;

            if (_state is FleetKafkaPushReadinessState.Starting or FleetKafkaPushReadinessState.Rebalancing)
                _state = FleetKafkaPushReadinessState.Assigned;

            _initialPositionOffset = nextOffsetToConsume;
            _currentResumeOffset = nextOffsetToConsume;
            _assignmentPositionEstablished = true;
        }
    }

    public void EstablishResumePosition(long nextOffsetToConsume)
    {
        lock (_sync)
        {
            if (_state == FleetKafkaPushReadinessState.Faulted)
                return;

            if (_state is FleetKafkaPushReadinessState.Starting or FleetKafkaPushReadinessState.Rebalancing)
                _state = FleetKafkaPushReadinessState.Assigned;

            _currentResumeOffset = nextOffsetToConsume;
            _assignmentPositionEstablished = true;
        }
    }

    // Compatibilidad con tests/arranque legacy.
    public void EstablishInitialPosition(long nextOffsetToConsume) =>
        EstablishFirstAssignmentPosition(nextOffsetToConsume);

    public void MarkReady()
    {
        lock (_sync)
        {
            if (_state == FleetKafkaPushReadinessState.Faulted)
                return;

            if (!_assignmentPositionEstablished)
            {
                throw new InvalidOperationException(
                    "Cannot mark Kafka push Ready before establishing the resume position.");
            }

            if (_state is not (
                FleetKafkaPushReadinessState.Assigned
                or FleetKafkaPushReadinessState.Ready))
            {
                return;
            }

            _state = FleetKafkaPushReadinessState.Ready;
            _hasCompletedFirstAssignment = true;
        }
    }

    public void MarkFaulted(string reason)
    {
        lock (_sync)
        {
            _state = FleetKafkaPushReadinessState.Faulted;
            _faultReason = reason;
            _assignmentPositionEstablished = false;
        }
    }

    public void MarkBypassed()
    {
        lock (_sync)
        {
            _initialPositionOffset = null;
            _currentResumeOffset = null;
            _assignmentPositionEstablished = true;
            _hasCompletedFirstAssignment = true;
            _faultReason = null;
            _state = FleetKafkaPushReadinessState.Ready;
        }
    }
}
