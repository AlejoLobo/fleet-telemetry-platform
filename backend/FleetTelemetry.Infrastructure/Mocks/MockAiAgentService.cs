using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Mocks;

public class MockAiAgentService : IAiAgentService
{
    private readonly ILogger<MockAiAgentService> _logger;

    public MockAiAgentService(ILogger<MockAiAgentService> logger)
    {
        _logger = logger;
    }

    public Task<AiQueryResponse> QueryAsync(AiQueryRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] AiAgent QueryAsync called with question: {Question}", request.Question);

        var response = new AiQueryResponse(
            Answer: "Respuesta simulada del agente IA. El agente operativo con herramientas internas está activo en modo de demostración.",
            Sources: ["mock-ai-agent", "phase-1-stub"]);

        return Task.FromResult(response);
    }
}
