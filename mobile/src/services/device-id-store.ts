import * as SecureStore from "expo-secure-store";
import * as Crypto from "expo-crypto";

const DEVICE_ID_KEY = "fleet.device.id";

/** UUID canónico con guiones (identidad técnica; no VH-###). */
const UUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

let cachedDeviceId: string | null = null;
let deviceIdPromise: Promise<string> | null = null;

export function isValidDeviceId(value: string | null | undefined): boolean {
  if (!value) return false;
  const trimmed = value.trim();
  if (!trimmed) return false;
  // Nombres visibles (VH-###, texto libre) no son identidad técnica.
  return UUID_REGEX.test(trimmed);
}

/** Genera un UUID estable para el dispositivo físico. */
export async function generateDeviceId(): Promise<string> {
  return Crypto.randomUUID();
}

/**
 * ID estable del dispositivo físico; no cambia con vehículo/conductor.
 * Usa caché en memoria + promesa compartida para llamadas concurrentes.
 */
export async function loadOrCreateDeviceId(): Promise<string> {
  if (cachedDeviceId && isValidDeviceId(cachedDeviceId)) {
    return cachedDeviceId;
  }

  if (!deviceIdPromise) {
    deviceIdPromise = resolveDeviceId().finally(() => {
      // Conserva la promesa solo mientras está en vuelo; el resultado vive en cachedDeviceId.
      deviceIdPromise = null;
    });
  }

  return deviceIdPromise;
}

async function resolveDeviceId(): Promise<string> {
  if (cachedDeviceId && isValidDeviceId(cachedDeviceId)) {
    return cachedDeviceId;
  }

  try {
    const existing = await SecureStore.getItemAsync(DEVICE_ID_KEY);
    if (existing && isValidDeviceId(existing)) {
      cachedDeviceId = existing.trim();
      return cachedDeviceId;
    }
    // Valor inválido (p. ej. VH-001): se reemplaza más abajo.
  } catch {
    if (cachedDeviceId && isValidDeviceId(cachedDeviceId)) {
      return cachedDeviceId;
    }
  }

  const created = await generateDeviceId();
  cachedDeviceId = created;

  try {
    await SecureStore.setItemAsync(DEVICE_ID_KEY, created);
  } catch {
    // Conserva el mismo UUID en memoria durante todo el proceso.
  }

  return created;
}

/** Solo pruebas: borra SecureStore, caché y promesa en vuelo. */
export async function resetDeviceIdForTests(): Promise<void> {
  cachedDeviceId = null;
  deviceIdPromise = null;
  try {
    await SecureStore.deleteItemAsync(DEVICE_ID_KEY);
  } catch {
    // Ignorar fallo de SecureStore en reset de pruebas.
  }
}
