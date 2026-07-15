import * as SecureStore from "expo-secure-store";

const VEHICLE_NAME_KEY = "fleet.device.vehicleName";
const REGISTERED_DEVICE_KEY = "fleet.device.registeredId";

/** Nombre de visualización cacheado; no es identidad técnica. */
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

export async function loadRegisteredDeviceId(): Promise<string | null> {
  try {
    const value = await SecureStore.getItemAsync(REGISTERED_DEVICE_KEY);
    const trimmed = value?.trim() ?? "";
    return trimmed.length > 0 ? trimmed : null;
  } catch {
    return null;
  }
}

export async function markDeviceRegistered(deviceId: string, vehicleName: string): Promise<void> {
  const id = deviceId.trim();
  if (!id) return;
  try {
    await SecureStore.setItemAsync(REGISTERED_DEVICE_KEY, id);
  } catch {
    // Registro remoto ya ocurrió; el cache local es best-effort.
  }
  await saveCachedVehicleName(vehicleName);
}

export async function clearDeviceProfileForTests(): Promise<void> {
  await SecureStore.deleteItemAsync(VEHICLE_NAME_KEY);
  await SecureStore.deleteItemAsync(REGISTERED_DEVICE_KEY);
}
