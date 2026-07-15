/** Intervalos permitidos de captura de telemetría en el dispositivo. */

export const TELEMETRY_CAPTURE_INTERVAL_OPTIONS_SECONDS = [3, 5, 10, 15] as const;

export type TelemetryCaptureIntervalSeconds =
  (typeof TELEMETRY_CAPTURE_INTERVAL_OPTIONS_SECONDS)[number];

export const DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS: TelemetryCaptureIntervalSeconds = 5;

export function isTelemetryCaptureIntervalSeconds(
  value: unknown,
): value is TelemetryCaptureIntervalSeconds {
  return (
    typeof value === "number"
    && (TELEMETRY_CAPTURE_INTERVAL_OPTIONS_SECONDS as readonly number[]).includes(value)
  );
}

export function parseTelemetryCaptureIntervalSeconds(
  raw: string | number | null | undefined,
): TelemetryCaptureIntervalSeconds {
  const parsed = typeof raw === "number" ? raw : Number(raw);
  return isTelemetryCaptureIntervalSeconds(parsed)
    ? parsed
    : DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS;
}
