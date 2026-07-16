import * as SecureStore from "expo-secure-store";
import {
  DEFAULT_VEHICLE_TYPE,
  normalizeVehicleType,
  type VehicleType,
} from "@/types/vehicle";

const VEHICLE_NAME_KEY = "fleet.device.vehicleName";
const VEHICLE_TYPE_KEY = "fleet.profile.vehicleType";
/** Clave obsoleta: no probar registro remoto con caché local. */
const LEGACY_REGISTERED_DEVICE_KEY = "fleet.device.registeredId";

export type LocalDeviceProfile = {
  vehicleName: string | null;
  vehicleType: VehicleType;
};

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

/** Restaura o elimina el nombre cacheado según el valor previo. */
export async function restoreCachedVehicleName(vehicleName: string | null): Promise<void> {
  try {
    if (vehicleName == null || vehicleName.trim() === "") {
      await SecureStore.deleteItemAsync(VEHICLE_NAME_KEY);
      return;
    }
    await SecureStore.setItemAsync(VEHICLE_NAME_KEY, vehicleName.trim());
  } catch (error) {
    throw error;
  }
}

export async function loadCachedVehicleType(): Promise<VehicleType> {
  try {
    const value = await SecureStore.getItemAsync(VEHICLE_TYPE_KEY);
    if (value == null || value.trim() === "") return DEFAULT_VEHICLE_TYPE;
    return normalizeVehicleType(value);
  } catch {
    return DEFAULT_VEHICLE_TYPE;
  }
}

export async function saveCachedVehicleType(vehicleType: VehicleType): Promise<void> {
  const normalized = normalizeVehicleType(vehicleType);
  try {
    await SecureStore.setItemAsync(VEHICLE_TYPE_KEY, normalized);
  } catch {
    // Best-effort.
  }
}

export async function loadLocalDeviceProfile(): Promise<LocalDeviceProfile> {
  const [vehicleName, vehicleType] = await Promise.all([
    loadCachedVehicleName(),
    loadCachedVehicleType(),
  ]);
  return { vehicleName, vehicleType };
}

/** Restaura nombre y tipo al estado previo (incluye nombre null). */
export async function restoreLocalDeviceProfile(previous: LocalDeviceProfile): Promise<void> {
  await restoreCachedVehicleName(previous.vehicleName);
  await saveCachedVehicleType(previous.vehicleType);
}

/**
 * Tras registro/perfil remoto exitoso: guarda nombre y tipo visibles en caché.
 * No escribe DeviceId como “prueba” de registro (el servidor es la fuente de verdad).
 */
export async function markDeviceRegistered(
  _deviceId: string,
  vehicleName: string,
  vehicleType: VehicleType = DEFAULT_VEHICLE_TYPE,
): Promise<void> {
  await saveCachedVehicleName(vehicleName);
  await saveCachedVehicleType(normalizeVehicleType(vehicleType));
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
    await SecureStore.deleteItemAsync(VEHICLE_TYPE_KEY);
  } catch {
    // ignore
  }
  try {
    await SecureStore.deleteItemAsync(LEGACY_REGISTERED_DEVICE_KEY);
  } catch {
    // ignore
  }
}
