using System.Text.RegularExpressions;

namespace FleetTelemetry.Application.Services;

public sealed record AiPromptGuardResult(
    bool IsSafe,
    string SanitizedQuestion,
    string? RejectionReason);

public static class AiPromptGuard
{
    private const int MaxQuestionLength = 500;

    private static readonly string[] InjectionPatterns =
    [
        "ignore previous",
        "ignore all previous",
        "disregard previous",
        "forget previous",
        "override instructions",
        "system prompt",
        "you are now",
        "act as",
        "pretend to be",
        "jailbreak",
        "dan mode",
        "developer mode",
        "reveal prompt",
        "show prompt",
        "bypass",
        "ignore las instrucciones",
        "ignora las instrucciones",
        "olvida las instrucciones",
        "nuevo rol",
        "modo desarrollador"
    ];

    private static readonly Regex ControlCharsRegex = new(
        @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
        RegexOptions.Compiled);

    private static readonly Regex RepeatedNewlinesRegex = new(
        @"\n{3,}",
        RegexOptions.Compiled);

    public static AiPromptGuardResult Inspect(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return new(false, string.Empty, "La pregunta está vacía.");

        var trimmed = question.Trim();
        if (trimmed.Length > MaxQuestionLength)
            return new(false, trimmed[..MaxQuestionLength], "La pregunta excede la longitud máxima permitida.");

        var normalized = trimmed.ToLowerInvariant();
        foreach (var pattern in InjectionPatterns)
        {
            if (normalized.Contains(pattern, StringComparison.Ordinal))
                return new(false, Sanitize(trimmed), "Se detectó un patrón de inyección de prompt no permitido.");
        }

        if (ContainsRolePlayMarkers(normalized))
            return new(false, Sanitize(trimmed), "Se detectó un intento de redefinir el rol del agente.");

        return new(true, Sanitize(trimmed), null);
    }

    private static bool ContainsRolePlayMarkers(string normalized) =>
        normalized.Contains("###", StringComparison.Ordinal)
        || normalized.Contains("```system", StringComparison.Ordinal)
        || normalized.Contains("<system>", StringComparison.Ordinal)
        || normalized.Contains("[system]", StringComparison.Ordinal);

    private static string Sanitize(string question)
    {
        var withoutControl = ControlCharsRegex.Replace(question, " ");
        return RepeatedNewlinesRegex.Replace(withoutControl, "\n\n").Trim();
    }
}
