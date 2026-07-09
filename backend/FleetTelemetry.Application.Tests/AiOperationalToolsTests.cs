using FleetTelemetry.Application.Services;

namespace FleetTelemetry.Application.Tests;

public class AiOperationalToolsTests
{
    [Theory]
    [InlineData("VH-001 está detenido", "VH-001")]
    [InlineData("estado de vh-002", "VH-002")]
    public void ExtractVehicleId_parses_id(string question, string expected)
    {
        var id = AiOperationalTools.ExtractVehicleId(question);
        Assert.Equal(expected, id);
    }

    [Fact]
    public void ParseSpeedThreshold_reads_number_from_question()
    {
        var threshold = AiOperationalTools.ParseSpeedThreshold("vehículos por encima de 95 km/h");
        Assert.Equal(95, threshold);
    }
}
