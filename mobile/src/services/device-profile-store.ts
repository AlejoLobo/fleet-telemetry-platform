import * as SecureStore from "expo-secure-store";

const VEHICLE_NAME_KEY = "fleet.device.vehicleName";
/** Clave obsoleta: no probar registro remoto con caché local. */
const LEGACY_REGISTERED_DEVICE_KEY = "fleet.device.registeredId";

/** Nombre de visualización cacheado; no es identidad técnica ni prueba de registro. */
export async function loadCachedVehicleName(): Promise<string | null> {
  try {
    const value = await SecureStore.getItemAsync(VEHICLE_NAME_KEY);
    const trimmed = value?.trim() ?? "";
    return trimmed.length > 0 ? trimmed : null;
  } catch {
    return null;
  }
}

export async function saveCachedVehicleName(vehicleName: string): Promise<void> {
  const trimmed = vehicleName.trim();
  if (!trimmed) return;
  try {
    await SecureStore.setItemAsync(VEHICLE_NAME_KEY, trimmed);
  } catch {
    // El nombre en memoria de UI sigue siendo usable aunque falle SecureStore.
  }
}

/**
 * Tras registro/rename remoto exitoso: guarda solo el nombre visible en caché.
 * No escribe DeviceId como “prueba” de registro (el servidor es la fuente de verdad).
 */
export async function markDeviceRegistered(_deviceId: string, vehicleName: string): Promise<void> {
  await saveCachedVehicleName(vehicleName);
  try {
    await SecureStore.deleteItemAsync(LEGACY_REGISTERED_DEVICE_KEY);
  } catch {
    // Best-effort: limpia clave obsoleta si existía.
  }
}

export async function clearDeviceProfileForTests(): Promise<void> {
  try {
    await SecureStore.deleteItemAsync(VEHICLE_NAME_KEY);
  } catch {
    // ignore
  }
  try {
    await SecureStore.deleteItemAsync(LEGACY_REGISTERED_DEVICE_KEY);
  } catch {
    // ignore
  }
}
