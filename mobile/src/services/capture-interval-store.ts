import * as SecureStore from "expo-secure-store";
import {
  DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS,
  parseTelemetryCaptureIntervalSeconds,
  type TelemetryCaptureIntervalSeconds,
} from "@/config/telemetry-capture-rate";

const CAPTURE_INTERVAL_KEY = "fleet.profile.captureIntervalSeconds";

/** Lee la frecuencia de captura persistida (3/5/10/15); inválido → 5. */
export async function loadCaptureIntervalSeconds(): Promise<TelemetryCaptureIntervalSeconds> {
  try {
    const raw = await SecureStore.getItemAsync(CAPTURE_INTERVAL_KEY);
    return parseTelemetryCaptureIntervalSeconds(raw);
  } catch {
    return DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS;
  }
}

/** Persiste la frecuencia de captura tras validar el valor. */
export async function saveCaptureIntervalSeconds(
  seconds: TelemetryCaptureIntervalSeconds,
): Promise<TelemetryCaptureIntervalSeconds> {
  const validated = parseTelemetryCaptureIntervalSeconds(seconds);
  await SecureStore.setItemAsync(CAPTURE_INTERVAL_KEY, String(validated));
  return validated;
}

/** Solo pruebas: limpia la preferencia de captura. */
export async function resetCaptureIntervalForTests(): Promise<void> {
  await SecureStore.deleteItemAsync(CAPTURE_INTERVAL_KEY);
}
