/** Tasa de refresco visual del monitor (no afecta captura móvil ni SSE). */

export type MonitorRefreshRate = "realtime" | 5 | 10 | 30 | 60;

export const MONITOR_REFRESH_RATE_STORAGE_KEY = "fleet-monitor-refresh-rate";

export const MONITOR_REFRESH_RATE_OPTIONS: ReadonlyArray<{
  value: MonitorRefreshRate;
  label: string;
}> = [
  { value: "realtime", label: "Tiempo real" },
  { value: 5, label: "Cada 5 segundos" },
  { value: 10, label: "Cada 10 segundos" },
  { value: 30, label: "Cada 30 segundos" },
  { value: 60, label: "Cada 1 minuto" },
];

const VALID_NUMERIC = new Set([5, 10, 30, 60]);

/** Interpreta un valor crudo; inválido → "realtime". */
export function parseMonitorRefreshRate(value: unknown): MonitorRefreshRate {
  if (value === "realtime") return "realtime";
  if (typeof value === "number" && VALID_NUMERIC.has(value)) {
    return value as 5 | 10 | 30 | 60;
  }
  if (typeof value === "string") {
    if (value === "realtime") return "realtime";
    const parsed = Number(value);
    if (VALID_NUMERIC.has(parsed)) {
      return parsed as 5 | 10 | 30 | 60;
    }
  }
  return "realtime";
}

/** Restaura la preferencia; seguro en SSR (sin window → realtime). */
export function loadMonitorRefreshRate(): MonitorRefreshRate {
  if (typeof window === "undefined") return "realtime";
  try {
    return parseMonitorRefreshRate(
      window.localStorage.getItem(MONITOR_REFRESH_RATE_STORAGE_KEY),
    );
  } catch {
    return "realtime";
  }
}

export function saveMonitorRefreshRate(rate: MonitorRefreshRate): void {
  if (typeof window === "undefined") return;
  try {
    window.localStorage.setItem(
      MONITOR_REFRESH_RATE_STORAGE_KEY,
      String(rate),
    );
  } catch {
    // localStorage no disponible (modo privado / cuota)
  }
}

/**
 * Milisegundos del ciclo visual.
 * realtime → null (aplicación inmediata de SSE; telemetría REST cada 5 s).
 */
export function monitorRefreshRateToMs(
  rate: MonitorRefreshRate,
): number | null {
  if (rate === "realtime") return null;
  return rate * 1000;
}

/** Intervalo de refresco de telemetría seleccionada en modo tiempo real. */
export const REALTIME_SELECTED_TELEMETRY_MS = 5_000;

/** Intervalo de regeneración demo en modo tiempo real. */
export const DEMO_REALTIME_REFRESH_MS = 5_000;
