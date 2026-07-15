using FleetTelemetry.Application.Services;

// Pruebas del parser de intención en preguntas IA.
namespace FleetTelemetry.Application.Tests;

public class AiQuestionParserTests
{
    [Theory]
    [InlineData("¿Qué vehículos llevan detenidos más de 20 minutos en zonas críticas?", AiQueryIntent.StoppedInCriticalZones, 20, true)]
    [InlineData("vehiculos parados mas de 30 min en kennedy", AiQueryIntent.StoppedInCriticalZones, 30, true)]
    [InlineData("detenidos veinte minutos zona critica", AiQueryIntent.StoppedInCriticalZones, 20, true)]
    [InlineData("cuales estan quietos mas de 45 min", AiQueryIntent.StoppedLongerThan, 45, false)]
    [InlineData("vehículos detenidos", AiQueryIntent.StoppedVehicles, null, false)]
    [InlineData("alertas críticas abiertas", AiQueryIntent.CriticalAlerts, null, false)]
    [InlineData("exceso de velocidad 95 km/h", AiQueryIntent.SpeedAbove, null, false)]
    [InlineData("estado de 33333333-3333-3333-3333-333333333333", AiQueryIntent.VehicleStatus, null, false)]
    public void Parse_detects_intent(
        string question,
        AiQueryIntent expectedIntent,
        int? expectedMinutes,
        bool expectedCritical)
    {
        var intent = AiQuestionParser.Parse(question);

        Assert.Equal(expectedIntent, intent.Intent);
        Assert.Equal(expectedMinutes, intent.StoppedMinutes);
        Assert.Equal(expectedCritical, intent.CriticalZonesOnly);
    }

    [Fact]
    public void ParseStoppedMinutes_reads_spanish_number()
    {
        Assert.Equal(20, AiQuestionParser.ParseStoppedMinutes("detenidos veinte minutos"));
    }

    [Fact]
    public void ExtractZoneName_finds_kennedy()
    {
        var zone = AiQuestionParser.ExtractZoneName("vehiculos parados en kennedy");
        Assert.Equal("Kennedy", zone);
    }

    [Theory]
    [InlineData("cuéntame un chiste")]
    [InlineData("¿qué tiempo hace hoy?")]
    [InlineData("hola, cómo estás")]
    public void Parse_returns_unsupported_for_non_operational_questions(string question)
    {
        var intent = AiQuestionParser.Parse(question);

        Assert.Equal(AiQueryIntent.UnsupportedQuery, intent.Intent);
    }
}
