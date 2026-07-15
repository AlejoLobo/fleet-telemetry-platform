import * as SecureStore from "expo-secure-store";
import * as Crypto from "expo-crypto";

const DEVICE_ID_KEY = "fleet.device.id";
const NIL_UUID = "00000000-0000-0000-0000-000000000000";

/** UUID canónico con guiones (identidad técnica; no VH-###). */
const UUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

let cachedDeviceId: string | null = null;
let deviceIdPromise: Promise<string> | null = null;

export class DeviceIdentityStorageError extends Error {
  constructor(
    public readonly operation: "read" | "write",
    message: string,
    options?: ErrorOptions,
  ) {
    super(message, options);
    this.name = "DeviceIdentityStorageError";
  }
}

export function isValidDeviceId(value: string | null | undefined): boolean {
  if (!value) return false;
  const trimmed = value.trim();
  if (!trimmed) return false;
  if (!UUID_REGEX.test(trimmed)) return false;
  if (trimmed.toLowerCase() === NIL_UUID) return false;
  return true;
}

/** Genera un UUID estable para el dispositivo físico. */
export async function generateDeviceId(): Promise<string> {
  return Crypto.randomUUID();
}

/**
 * ID estable del dispositivo físico.
 * Solo genera UUID nuevo si SecureStore respondió OK y no había valor.
 * Fallos de lectura/escritura sin caché bloquean identidad (no regeneran).
 */
export async function loadOrCreateDeviceId(): Promise<string> {
  if (cachedDeviceId && isValidDeviceId(cachedDeviceId)) {
    return cachedDeviceId;
  }

  if (!deviceIdPromise) {
    deviceIdPromise = resolveDeviceId().finally(() => {
      deviceIdPromise = null;
    });
  }

  return deviceIdPromise;
}

async function resolveDeviceId(): Promise<string> {
  if (cachedDeviceId && isValidDeviceId(cachedDeviceId)) {
    return cachedDeviceId;
  }

  let stored: string | null;
  try {
    stored = await SecureStore.getItemAsync(DEVICE_ID_KEY);
  } catch (error) {
    if (cachedDeviceId && isValidDeviceId(cachedDeviceId)) {
      return cachedDeviceId;
    }
    throw new DeviceIdentityStorageError(
      "read",
      "No se pudo leer la identidad del dispositivo desde SecureStore",
      { cause: error },
    );
  }

  if (stored && isValidDeviceId(stored)) {
    cachedDeviceId = stored.trim();
    return cachedDeviceId;
  }

  // Valor inválido o ausente tras lectura OK → generar y persistir.
  const created = await generateDeviceId();

  try {
    await SecureStore.setItemAsync(DEVICE_ID_KEY, created);
  } catch (error) {
    // No confirmar identidad en memoria si la escritura falló.
    cachedDeviceId = null;
    throw new DeviceIdentityStorageError(
      "write",
      "No se pudo persistir la identidad del dispositivo en SecureStore",
      { cause: error },
    );
  }

  cachedDeviceId = created;
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

/** Expone caché solo para tests de fallos de lectura. */
export function setCachedDeviceIdForTests(deviceId: string | null): void {
  cachedDeviceId = deviceId;
}
