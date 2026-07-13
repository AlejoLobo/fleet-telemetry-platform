/** Error lanzado cuando el resync estricto del dashboard no puede completarse. */
export class ResyncFailedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ResyncFailedError";
  }
}

/** Parsea el payload de stream-reset emitido por la API. */
export function parseStreamResetPayload(data: string): { reason: string; latestEventId: number | null } | null {
  try {
    const parsed = JSON.parse(data) as { reason?: string; latestEventId?: number | null };
    if (!parsed || typeof parsed.reason !== "string") return null;
    const latestEventId = parsed.latestEventId === null || parsed.latestEventId === undefined
      ? null
      : Number(parsed.latestEventId);
    if (latestEventId !== null && (!Number.isFinite(latestEventId) || latestEventId < 0)) {
      return null;
    }
    return { reason: parsed.reason, latestEventId };
  } catch {
    return null;
  }
}
