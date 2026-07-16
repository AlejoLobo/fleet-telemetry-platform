import {
  registerDevice,
  updateDeviceProfile,
  type DeviceProfile,
} from "@/services/device-api";
import {
  loadCachedVehicleName,
  loadCachedVehicleType,
  loadLocalDeviceProfile,
  markDeviceRegistered,
  saveCachedVehicleName,
  saveCachedVehicleType,
} from "@/services/device-profile-store";
import { TelemetryApiError } from "@/services/telemetry-api";
import {
  DEFAULT_VEHICLE_TYPE,
  normalizeVehicleType,
  type VehicleType,
} from "@/types/vehicle";

type InflightEntry = {
  promise: Promise<DeviceProfile>;
};

const registerInflight = new Map<string, InflightEntry>();

/**
 * Confirma registro remoto antes de sync (endpoint idempotente).
 * La caché SecureStore solo aporta el nombre/tipo offline; no demuestra registro servidor.
 */
export async function ensureDeviceRegistered(
  deviceId: string,
  vehicleType?: VehicleType,
): Promise<DeviceProfile> {
  const id = deviceId.trim();
  if (!id) {
    throw new Error("deviceId vacío");
  }

  const existing = registerInflight.get(id);
  if (existing) {
    return existing.promise;
  }

  const promise = (async () => {
    const type =
      vehicleType != null
        ? normalizeVehicleType(vehicleType)
        : await loadCachedVehicleType();
    const profile = await registerDevice(id, type);
    await markDeviceRegistered(profile.deviceId, profile.vehicleName, profile.vehicleType);
    return profile;
  })();

  registerInflight.set(id, { promise });
  try {
    return await promise;
  } finally {
    registerInflight.delete(id);
  }
}

/** Actualiza nombre y tipo visibles; DeviceId permanece inmutable. */
export async function updateVehicleProfile(
  deviceId: string,
  profile: { vehicleName: string; vehicleType: VehicleType },
): Promise<DeviceProfile> {
  const id = deviceId.trim();
  const previous = await loadLocalDeviceProfile();

  try {
    const remote = await updateDeviceProfile(id, {
      vehicleName: profile.vehicleName,
      vehicleType: profile.vehicleType,
    });
    await markDeviceRegistered(remote.deviceId, remote.vehicleName, remote.vehicleType);
    return remote;
  } catch (error) {
    if (error instanceof TelemetryApiError && error.status === 404) {
      await ensureDeviceRegistered(id, profile.vehicleType);
      try {
        const remote = await updateDeviceProfile(id, {
          vehicleName: profile.vehicleName,
          vehicleType: profile.vehicleType,
        });
        await markDeviceRegistered(remote.deviceId, remote.vehicleName, remote.vehicleType);
        return remote;
      } catch (retryError) {
        await restorePreviousProfile(previous);
        throw retryError;
      }
    }

    if (error instanceof TelemetryApiError && (error.status === 409 || error.status === 400)) {
      await restorePreviousProfile(previous);
      throw error;
    }

    throw error;
  }
}

/** @deprecated Preferir updateVehicleProfile; conserva compatibilidad de nombre. */
export async function updateVehicleDisplayName(
  deviceId: string,
  vehicleName: string,
): Promise<DeviceProfile> {
  const type = await loadCachedVehicleType();
  return updateVehicleProfile(deviceId, {
    vehicleName,
    vehicleType: type || DEFAULT_VEHICLE_TYPE,
  });
}

async function restorePreviousProfile(previous: {
  vehicleName: string | null;
  vehicleType: VehicleType;
}): Promise<void> {
  if (previous.vehicleName) await saveCachedVehicleName(previous.vehicleName);
  await saveCachedVehicleType(previous.vehicleType);
}

/** Solo pruebas: limpia promesas en vuelo. */
export function resetDeviceRegistryForTests(): void {
  registerInflight.clear();
}
