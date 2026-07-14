namespace FleetTelemetry.Infrastructure.Realtime;

// Transiciones Idle/Completed/Transient/Fatal del pump (testeable sin hilos).
internal readonly record struct KafkaPushPollTransition(
    long? BaselineForReady,
    bool RecreateConsumer);

internal static class KafkaPushPollStateMachine
{
    public static KafkaPushPollTransition Apply(
        IRealtimeStreamCoordinator coordinator,
        KafkaPushPollResult pollResult,
        long? baselineForReady)
    {
        switch (pollResult)
        {
            case KafkaPushPollResult.Idle:
            case KafkaPushPollResult.Completed:
                if (coordinator.State is RealtimeStreamState.Starting or RealtimeStreamState.Recovering)
                    coordinator.EnterReady(baselineForReady);
                return new KafkaPushPollTransition(BaselineForReady: null, RecreateConsumer: false);

            case KafkaPushPollResult.TransientFailure:
                if (coordinator.State == RealtimeStreamState.Ready)
                    coordinator.EnterRecovering("Transient publish failure");
                return new KafkaPushPollTransition(baselineForReady, RecreateConsumer: false);

            case KafkaPushPollResult.FatalFailure:
                if (coordinator.State != RealtimeStreamState.Faulted)
                    coordinator.EnterRecovering("Fatal Kafka transport failure");
                return new KafkaPushPollTransition(baselineForReady, RecreateConsumer: true);

            default:
                return new KafkaPushPollTransition(baselineForReady, RecreateConsumer: false);
        }
    }
}
