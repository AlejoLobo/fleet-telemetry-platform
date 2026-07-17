/** Error lanzado cuando el resync estricto del dashboard no puede completarse. */
export class ResyncFailedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ResyncFailedError";
  }
}

/** Error cuando un resync o carga quedó obsoleto por otra generación. */
export class ResyncSupersededError extends Error {
  constructor(message = "Resync superseded by a newer snapshot generation.") {
    super(message);
    this.name = "ResyncSupersededError";
  }
}

/** Regex para IDs decimales de offset Kafka (64-bit seguro como string). */
export const DECIMAL_EVENT_ID_PATTERN = /^(0|[1-9]\d*)$/;

export type StreamResetPayload = {
  reason: string;
  latestEventId: string | null;
};

/** Valida y normaliza un ID de evento decimal sin conversión numérica. */
export function parseDecimalEventId(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value === "number") return null;
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  if (!DECIMAL_EVENT_ID_PATTERN.test(trimmed)) return null;
  return trimmed;
}

/** Parsea el payload de stream-reset emitido por la API. */
export function parseStreamResetPayload(data: string): StreamResetPayload | null {
  try {
    const parsed = JSON.parse(data) as { reason?: unknown; latestEventId?: unknown };
    if (!parsed || typeof parsed.reason !== "string") return null;

    if (parsed.latestEventId === null || parsed.latestEventId === undefined) {
      return { reason: parsed.reason, latestEventId: null };
    }

    const latestEventId = parseDecimalEventId(
      typeof parsed.latestEventId === "number"
        ? null
        : String(parsed.latestEventId),
    );
    if (latestEventId === null) return null;

    return { reason: parsed.reason, latestEventId };
  } catch {
    return null;
  }
}

export type ResyncSnapshotResult = {
  resolvedDeviceId: string | null;
  applied: true;
};
