import { registerDevice, renameDevice, type DeviceProfile } from "@/services/device-api";
import {
  loadCachedVehicleName,
  markDeviceRegistered,
  saveCachedVehicleName,
} from "@/services/device-profile-store";
import { TelemetryApiError } from "@/services/telemetry-api";

type InflightEntry = {
  promise: Promise<DeviceProfile>;
};

const registerInflight = new Map<string, InflightEntry>();

/**
 * Confirma registro remoto antes de sync (endpoint idempotente).
 * La caché SecureStore solo aporta el nombre offline; no demuestra registro servidor.
 */
export async function ensureDeviceRegistered(deviceId: string): Promise<DeviceProfile> {
  const id = deviceId.trim();
  if (!id) {
    throw new Error("deviceId vacío");
  }

  const existing = registerInflight.get(id);
  if (existing) {
    return existing.promise;
  }

  const promise = (async () => {
    const profile = await registerDevice(id);
    // Una sola escritura de nombre; la caché no demuestra registro remoto.
    await markDeviceRegistered(profile.deviceId, profile.vehicleName);
    return profile;
  })();

  registerInflight.set(id, { promise });
  try {
    return await promise;
  } finally {
    registerInflight.delete(id);
  }
}

/** Actualiza el nombre visible; DeviceId permanece inmutable. */
export async function updateVehicleDisplayName(
  deviceId: string,
  vehicleName: string,
): Promise<DeviceProfile> {
  const id = deviceId.trim();
  const previousName = await loadCachedVehicleName();

  try {
    const profile = await renameDevice(id, vehicleName);
    await markDeviceRegistered(profile.deviceId, profile.vehicleName);
    return profile;
  } catch (error) {
    if (error instanceof TelemetryApiError && error.status === 404) {
      await ensureDeviceRegistered(id);
      try {
        const profile = await renameDevice(id, vehicleName);
        await markDeviceRegistered(profile.deviceId, profile.vehicleName);
        return profile;
      } catch (retryError) {
        if (previousName) await saveCachedVehicleName(previousName);
        throw retryError;
      }
    }

    if (error instanceof TelemetryApiError && error.status === 409) {
      // Conserva el nombre local anterior ante conflicto.
      if (previousName) await saveCachedVehicleName(previousName);
      throw error;
    }

    throw error;
  }
}

/** Solo pruebas: limpia promesas en vuelo. */
export function resetDeviceRegistryForTests(): void {
  registerInflight.clear();
}
