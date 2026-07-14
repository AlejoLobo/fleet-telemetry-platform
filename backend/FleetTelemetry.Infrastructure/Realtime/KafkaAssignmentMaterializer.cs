using Confluent.Kafka;

namespace FleetTelemetry.Infrastructure.Realtime;

// Confirma que Assign quedó aplicado (posición / tip) sin Seek.
internal static class KafkaAssignmentMaterializer
{
    public static ConsumeResult<string, string>? Materialize(
        Func<TimeSpan, ConsumeResult<string, string>?> consume,
        Func<IReadOnlyList<TopicPartition>> getAssignment,
        Func<TopicPartition, Offset> getPosition,
        IReadOnlyDictionary<TopicPartition, long> assignedOffsets,
        TimeSpan timeout,
        string topic)
    {
        if (assignedOffsets.Count == 0)
        {
            throw new ArgumentException("Assign materialization requires at least one target offset.", nameof(assignedOffsets));
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var poll = remaining < TimeSpan.FromMilliseconds(100)
                ? remaining
                : TimeSpan.FromMilliseconds(100);

            var result = consume(poll);
            if (result is not null)
            {
                if (IsAtOrAfterAssigned(result, assignedOffsets))
                    return result;

                // Offset inferior al objetivo: descartar y seguir (sin Seek).
                continue;
            }

            // Idle solo es válido si Assignment/Position confirman el offset objetivo.
            if (IsTipConfirmed(getAssignment, getPosition, assignedOffsets))
                return null;
        }

        var first = assignedOffsets.First();
        throw new RealtimeKafkaAssignmentMaterializationException(
            topic,
            first.Key.Partition.Value,
            first.Value);
    }

    private static bool IsAtOrAfterAssigned(
        ConsumeResult<string, string> result,
        IReadOnlyDictionary<TopicPartition, long> assignedOffsets)
    {
        if (!assignedOffsets.TryGetValue(result.TopicPartition, out var assigned))
            return true;

        return result.Offset.Value >= assigned;
    }

    private static bool IsTipConfirmed(
        Func<IReadOnlyList<TopicPartition>> getAssignment,
        Func<TopicPartition, Offset> getPosition,
        IReadOnlyDictionary<TopicPartition, long> assignedOffsets)
    {
        var assignment = getAssignment();
        if (assignment.Count == 0)
            return false;

        foreach (var (partition, assignedOffset) in assignedOffsets)
        {
            // Evidencia mínima: la partición figura en Assignment tras un poll Idle.
            if (!assignment.Any(existing => existing.Equals(partition)))
                return false;

            var position = getPosition(partition);
            // Position Unset es habitual justo tras Assign materializado en el tip.
            if (position == Offset.Unset)
                continue;

            // Si el cliente ya expone Position, debe estar en o por delante del Assign.
            if (position.Value < assignedOffset)
                return false;
        }

        return true;
    }
}
