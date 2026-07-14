namespace FleetTelemetry.Infrastructure.Realtime;

// Resuelve Assign tras FatalFailure según watermarks Low/High.
internal readonly record struct KafkaAssignmentPlan(long AssignOffset, long? NewBaseline);

internal static class KafkaResumePosition
{
    // resumeOffset = lastProcessed + 1. Fuera de [Low, High] → nueva baseline en High-1.
    public static KafkaAssignmentPlan Resolve(long lastProcessedExternalOffset, long low, long high)
    {
        var resumeOffset = lastProcessedExternalOffset + 1;
        if (resumeOffset >= low && resumeOffset <= high)
            return new KafkaAssignmentPlan(resumeOffset, NewBaseline: null);

        return new KafkaAssignmentPlan(high, NewBaseline: high - 1);
    }
}
