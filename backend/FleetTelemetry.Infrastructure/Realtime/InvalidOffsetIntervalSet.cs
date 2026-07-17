namespace FleetTelemetry.Infrastructure.Realtime;

// Intervalos compactados de offsets Kafka inválidos (consulta en tiempo acotado).
internal sealed class InvalidOffsetIntervalSet
{
    private readonly List<(long Start, long End)> _intervals = [];
    private readonly int _maxOffsetsCovered;

    public InvalidOffsetIntervalSet(int maxOffsetsCovered = 10_000)
    {
        _maxOffsetsCovered = Math.Max(1, maxOffsetsCovered);
    }

    public int IntervalCount => _intervals.Count;

    public long CoveredOffsetCount
    {
        get
        {
            long total = 0;
            foreach (var (start, end) in _intervals)
                total += end - start + 1;
            return total;
        }
    }

    public bool Contains(long offset)
    {
        var index = FindIntervalIndex(offset);
        return index >= 0;
    }

    // Detecta cualquier offset inválido en (exclusiveStart, inclusiveEnd].
    public bool HasAnyInOpenClosedRange(long exclusiveStart, long inclusiveEnd)
    {
        if (inclusiveEnd <= exclusiveStart)
            return false;

        foreach (var (start, end) in _intervals)
        {
            if (end <= exclusiveStart)
                continue;
            if (start > inclusiveEnd)
                break;
            var overlapStart = Math.Max(start, exclusiveStart + 1);
            var overlapEnd = Math.Min(end, inclusiveEnd);
            if (overlapStart <= overlapEnd)
                return true;
        }

        return false;
    }

    public void Add(long offset)
    {
        if (_intervals.Count == 0)
        {
            _intervals.Add((offset, offset));
            TrimIfNeeded();
            return;
        }

        var index = FindInsertIndex(offset);
        if (index < _intervals.Count)
        {
            var (start, end) = _intervals[index];
            if (offset >= start && offset <= end)
                return;
        }

        if (index > 0)
        {
            var prev = _intervals[index - 1];
            if (offset == prev.End + 1)
            {
                var mergedEnd = prev.End + 1;
                if (index < _intervals.Count && _intervals[index].Start == mergedEnd + 1)
                {
                    _intervals[index - 1] = (prev.Start, _intervals[index].End);
                    _intervals.RemoveAt(index);
                }
                else
                {
                    _intervals[index - 1] = (prev.Start, mergedEnd);
                }

                TrimIfNeeded();
                return;
            }
        }

        if (index < _intervals.Count && offset == _intervals[index].Start - 1)
        {
            _intervals[index] = (offset, _intervals[index].End);
            TrimIfNeeded();
            return;
        }

        _intervals.Insert(index, (offset, offset));
        TrimIfNeeded();
    }

    public void PruneBeforeOrEqual(long maxRetainedExclusive)
    {
        while (_intervals.Count > 0 && _intervals[0].End <= maxRetainedExclusive)
            _intervals.RemoveAt(0);

        if (_intervals.Count > 0 && _intervals[0].Start <= maxRetainedExclusive)
            _intervals[0] = (maxRetainedExclusive + 1, _intervals[0].End);
    }

    public void Clear() => _intervals.Clear();

    private void TrimIfNeeded()
    {
        while (CoveredOffsetCount > _maxOffsetsCovered && _intervals.Count > 0)
        {
            var (start, end) = _intervals[0];
            if (start == end)
            {
                _intervals.RemoveAt(0);
                continue;
            }

            _intervals[0] = (start + 1, end);
        }
    }

    private int FindIntervalIndex(long offset)
    {
        var low = 0;
        var high = _intervals.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var (start, end) = _intervals[mid];
            if (offset < start)
                high = mid - 1;
            else if (offset > end)
                low = mid + 1;
            else
                return mid;
        }

        return -1;
    }

    private int FindInsertIndex(long offset)
    {
        var low = 0;
        var high = _intervals.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (_intervals[mid].Start < offset)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }
}
