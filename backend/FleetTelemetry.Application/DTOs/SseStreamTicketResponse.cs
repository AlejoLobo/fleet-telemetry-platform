namespace FleetTelemetry.Application.DTOs;

public record SseStreamTicketResponse(string Ticket, int ExpiresInSeconds);
