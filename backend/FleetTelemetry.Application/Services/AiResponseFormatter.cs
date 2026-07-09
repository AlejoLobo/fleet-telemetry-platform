using System.Text.RegularExpressions;

// Formateo y localización de respuestas del agente IA.
namespace FleetTelemetry.Application.Services;

// Traduce etiquetas y mensajes de alerta al español.
public static class AiResponseFormatter
{
    // Convierte estado online/offline a etiquetas legibles.
    public static string EtiquetaEstado(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "online" => "En línea",
            "offline" => "Desconectado",
            _ => status ?? "Desconocido"
        };

    // Traduce tipo de alerta interno a texto descriptivo.
    public static string EtiquetaTipoAlerta(string alertType) =>
        alertType.ToLowerInvariant() switch
        {
            "overspeed" => "Exceso de velocidad",
            "low_fuel" => "Combustible bajo",
            "low_battery" => "Batería baja",
            _ => alertType.Replace('_', ' ')
        };

    // Convierte mensajes de alerta en inglés a español.
    public static string TraducirMensajeAlerta(string vehicleId, string message)
    {
        var exceso = Regex.Match(message, @"exceeded speed limit:\s*([\d.,]+)\s*km/h", RegexOptions.IgnoreCase);
        if (exceso.Success)
        {
            var speed = exceso.Groups[1].Value.Replace(',', '.');
            return $"El vehículo {vehicleId} superó el límite de velocidad: {speed} km/h";
        }

        var combustible = Regex.Match(message, @"has low fuel:\s*([\d.,]+)%", RegexOptions.IgnoreCase);
        if (combustible.Success)
        {
            var level = combustible.Groups[1].Value.Replace(',', '.');
            return $"El vehículo {vehicleId} tiene combustible bajo: {level}%";
        }

        var bateria = Regex.Match(message, @"has low battery:\s*([\d.,]+)%", RegexOptions.IgnoreCase);
        if (bateria.Success)
        {
            var level = bateria.Groups[1].Value.Replace(',', '.');
            return $"El vehículo {vehicleId} tiene batería baja: {level}%";
        }

        return message;
    }

    // Aplica reemplazos de localización en respuestas completas.
    public static string LocalizarRespuesta(string answer) =>
        answer
            .Replace("Online:", "En línea:", StringComparison.OrdinalIgnoreCase)
            .Replace("Offline:", "Desconectado:", StringComparison.OrdinalIgnoreCase)
            .Replace("- Estado: online", "- Estado: En línea", StringComparison.OrdinalIgnoreCase)
            .Replace("- Estado: offline", "- Estado: Desconectado", StringComparison.OrdinalIgnoreCase);
}
