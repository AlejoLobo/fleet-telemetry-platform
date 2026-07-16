/** Tasa de refresco visual del monitor (no afecta captura móvil ni SSE). */

export type MonitorRefreshRate = 5 | 10 | 15 | 20;

export const DEFAULT_MONITOR_REFRESH_RATE: MonitorRefreshRate = 5;

export const MONITOR_REFRESH_RATE_STORAGE_KEY = "fleet-monitor-refresh-rate";

export const MONITOR_REFRESH_RATE_OPTIONS: ReadonlyArray<{
  value: MonitorRefreshRate;
  label: string;
}> = [
  { value: 5, label: "Cada 5 segundos" },
  { value: 10, label: "Cada 10 segundos" },
  { value: 15, label: "Cada 15 segundos" },
  { value: 20, label: "Cada 20 segundos" },
];

const VALID = new Set<number>([5, 10, 15, 20]);

/** Interpreta un valor crudo; inválido o legado (realtime/30/60) → 5. */
export function parseMonitorRefreshRate(value: unknown): MonitorRefreshRate {
  if (typeof value === "number" && VALID.has(value)) {
    return value as MonitorRefreshRate;
  }
  if (typeof value === "string") {
    const parsed = Number(value);
    if (VALID.has(parsed)) {
      return parsed as MonitorRefreshRate;
    }
  }
  return DEFAULT_MONITOR_REFRESH_RATE;
}

/** Restaura la preferencia; seguro en SSR (sin window → 5). */
export function loadMonitorRefreshRate(): MonitorRefreshRate {
  if (typeof window === "undefined") return DEFAULT_MONITOR_REFRESH_RATE;
  try {
    return parseMonitorRefreshRate(
      window.localStorage.getItem(MONITOR_REFRESH_RATE_STORAGE_KEY),
    );
  } catch {
    return DEFAULT_MONITOR_REFRESH_RATE;
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
    // localStorage no disponible
  }
}

/** Milisegundos del ciclo visual (siempre un intervalo fijo). */
export function monitorRefreshRateToMs(rate: MonitorRefreshRate): number {
  return rate * 1000;
}
