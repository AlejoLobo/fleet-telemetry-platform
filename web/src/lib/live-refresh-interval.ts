/** Intervalos de refresco del monitor en vivo. */

export const LIVE_REFRESH_INTERVAL_OPTIONS_SECONDS = [3, 5, 10, 15] as const;

export type LiveRefreshIntervalSeconds = (typeof LIVE_REFRESH_INTERVAL_OPTIONS_SECONDS)[number];

export const DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS: LiveRefreshIntervalSeconds = 5;

export const LIVE_REFRESH_INTERVAL_STORAGE_KEY = "fleet.monitor.liveRefreshSeconds";

export function isLiveRefreshIntervalSeconds(value: unknown): value is LiveRefreshIntervalSeconds {
  return (
    typeof value === "number"
    && (LIVE_REFRESH_INTERVAL_OPTIONS_SECONDS as readonly number[]).includes(value)
  );
}

export function parseLiveRefreshIntervalSeconds(
  raw: string | null | undefined,
): LiveRefreshIntervalSeconds {
  if (!raw) return DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS;
  const parsed = Number(raw);
  return isLiveRefreshIntervalSeconds(parsed)
    ? parsed
    : DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS;
}

export function readLiveRefreshIntervalSeconds(): LiveRefreshIntervalSeconds {
  if (typeof window === "undefined") return DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS;
  try {
    return parseLiveRefreshIntervalSeconds(
      window.localStorage.getItem(LIVE_REFRESH_INTERVAL_STORAGE_KEY),
    );
  } catch {
    return DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS;
  }
}

export function writeLiveRefreshIntervalSeconds(seconds: LiveRefreshIntervalSeconds): void {
  if (typeof window === "undefined") return;
  try {
    window.localStorage.setItem(LIVE_REFRESH_INTERVAL_STORAGE_KEY, String(seconds));
  } catch {
    // localStorage puede fallar en modo privado; la preferencia vive en memoria.
  }
}
