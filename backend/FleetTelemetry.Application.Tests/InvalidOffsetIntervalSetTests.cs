using System.Diagnostics;
using System.Text.Json;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

public class InvalidOffsetIntervalSetTests
{
    [Fact]
    public void HasAnyInOpenClosedRange_no_itera_linealmente()
    {
        var set = new InvalidOffsetIntervalSet();
        set.Add(5);
        set.Add(100);

        var sw = Stopwatch.StartNew();
        var found = set.HasAnyInOpenClosedRange(0, long.MaxValue / 4);
        sw.Stop();

        Assert.True(found);
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void Compacta_offsets_consecutivos()
    {
        var set = new InvalidOffsetIntervalSet();
        set.Add(10);
        set.Add(11);
        set.Add(12);

        Assert.Equal(1, set.IntervalCount);
        Assert.Equal(3, set.CoveredOffsetCount);
        Assert.True(set.HasAnyInOpenClosedRange(9, 12));
        Assert.False(set.HasAnyInOpenClosedRange(12, 12));
    }
}
