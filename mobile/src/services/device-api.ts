import { getApiBaseUrl } from "@/config/env";
import {
  categorizeHttpStatus,
  ensureTelemetryTransportReady,
  sanitizeErrorText,
  TelemetryApiError,
} from "@/services/telemetry-api";

const DEFAULT_TIMEOUT_MS = 15_000;

export type DeviceProfile = {
  deviceId: string;
  vehicleName: string;
};

function assertDeviceId(deviceId: string): string {
  const normalized = deviceId.trim();
  if (!normalized) {
    throw new TelemetryApiError(0, "protocol", "deviceId vacío");
  }
  return normalized;
}

function parseDeviceResponse(raw: unknown): DeviceProfile {
  if (!raw || typeof raw !== "object") {
    throw new TelemetryApiError(0, "protocol", "Respuesta de dispositivo inválida");
  }
  const record = raw as Record<string, unknown>;
  const deviceId = typeof record.deviceId === "string" ? record.deviceId.trim() : "";
  const vehicleName = typeof record.vehicleName === "string" ? record.vehicleName.trim() : "";
  if (!deviceId || !vehicleName) {
    throw new TelemetryApiError(0, "protocol", "Respuesta de dispositivo incompleta");
  }
  return { deviceId, vehicleName };
}

async function requestJson(
  method: "POST" | "PATCH",
  path: string,
  body: unknown,
  deviceId: string,
  timeoutMs = DEFAULT_TIMEOUT_MS,
): Promise<DeviceProfile> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const headers = await ensureTelemetryTransportReady(deviceId);
    const response = await fetch(`${getApiBaseUrl()}${path}`, {
      method,
      headers,
      body: JSON.stringify(body),
      signal: controller.signal,
    });

    const text = await response.text();
    if (!response.ok) {
      const retryAfter = Number(response.headers.get("Retry-After") ?? "");
      throw new TelemetryApiError(
        response.status,
        categorizeHttpStatus(response.status),
        sanitizeErrorText(text) || `HTTP ${response.status}`,
        Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
      );
    }

    const parsed = text ? JSON.parse(text) as unknown : null;
    return parseDeviceResponse(parsed);
  } catch (error) {
    if (error instanceof TelemetryApiError) throw error;
    if (error instanceof Error && error.name === "AbortError") {
      throw new TelemetryApiError(408, "timeout", "Timeout al llamar API de dispositivos");
    }
    if (error instanceof SyntaxError) {
      throw new TelemetryApiError(0, "protocol", "JSON de dispositivo inválido");
    }
    throw new TelemetryApiError(0, "network", error instanceof Error ? error.message : "Error de red");
  } finally {
    clearTimeout(timeout);
  }
}

/** Registra el dispositivo; el backend asigna VehicleName automático (idempotente). */
export async function registerDevice(deviceId: string): Promise<DeviceProfile> {
  const id = assertDeviceId(deviceId);
  return requestJson("POST", "/api/devices/register", { deviceId: id }, id);
}

/** Renombra el vehículo sin cambiar DeviceId ni partición Kafka. */
export async function renameDevice(deviceId: string, vehicleName: string): Promise<DeviceProfile> {
  const id = assertDeviceId(deviceId);
  const name = vehicleName.trim();
  if (!name) {
    throw new TelemetryApiError(400, "validation", "vehicleName vacío");
  }
  return requestJson("PATCH", `/api/devices/${encodeURIComponent(id)}/name`, { vehicleName: name }, id);
}
