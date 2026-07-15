import { registerDevice, renameDevice, type DeviceProfile } from "@/services/device-api";
import {
  loadCachedVehicleName,
  loadRegisteredDeviceId,
  markDeviceRegistered,
  saveCachedVehicleName,
} from "@/services/device-profile-store";

/**
 * Garantiza registro remoto antes de sync. El backend es idempotente y asigna VH-###.
 * Mobile nunca genera VH-### localmente.
 */
export async function ensureDeviceRegistered(deviceId: string): Promise<DeviceProfile> {
  const id = deviceId.trim();
  if (!id) {
    throw new Error("deviceId vacío");
  }

  const registeredId = await loadRegisteredDeviceId();
  const cachedName = await loadCachedVehicleName();
  if (registeredId === id && cachedName) {
    return { deviceId: id, vehicleName: cachedName };
  }

  const profile = await registerDevice(id);
  await markDeviceRegistered(profile.deviceId, profile.vehicleName);
  return profile;
}

/** Actualiza el nombre visible; DeviceId permanece inmutable. */
export async function updateVehicleDisplayName(
  deviceId: string,
  vehicleName: string,
): Promise<DeviceProfile> {
  await ensureDeviceRegistered(deviceId);
  const profile = await renameDevice(deviceId, vehicleName);
  await markDeviceRegistered(profile.deviceId, profile.vehicleName);
  await saveCachedVehicleName(profile.vehicleName);
  return profile;
}
