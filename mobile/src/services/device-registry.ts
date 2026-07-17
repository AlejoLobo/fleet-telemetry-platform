import {
  registerDevice,
  updateDeviceProfile,
  type DeviceProfile,
} from "@/services/device-api";
import {
  loadCachedVehicleType,
  loadLocalDeviceProfile,
  markDeviceRegistered,
  restoreLocalDeviceProfile,
  type LocalDeviceProfile,
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

async function executeUpdateWithRegistrationFallback(
  deviceId: string,
  profile: { vehicleName: string; vehicleType: VehicleType },
): Promise<DeviceProfile> {
  try {
    return await updateDeviceProfile(deviceId, {
      vehicleName: profile.vehicleName,
      vehicleType: profile.vehicleType,
    });
  } catch (error) {
    if (!(error instanceof TelemetryApiError) || error.status !== 404) {
      throw error;
    }

    await ensureDeviceRegistered(deviceId, profile.vehicleType);
    return updateDeviceProfile(deviceId, {
      vehicleName: profile.vehicleName,
      vehicleType: profile.vehicleType,
    });
  }
}

/**
 * Actualiza nombre y tipo visibles de forma atómica respecto a SecureStore.
 * DeviceId permanece inmutable. Ante cualquier fallo se restaura el perfil previo.
 */
export async function updateVehicleProfile(
  deviceId: string,
  profile: { vehicleName: string; vehicleType: VehicleType },
): Promise<DeviceProfile> {
  const id = deviceId.trim();
  const previous = await loadLocalDeviceProfile();

  try {
    const remote = await executeUpdateWithRegistrationFallback(id, profile);
    await markDeviceRegistered(remote.deviceId, remote.vehicleName, remote.vehicleType);
    return remote;
  } catch (error) {
    try {
      await restoreLocalDeviceProfile(previous);
    } catch (restoreError) {
      // Conserva el error original; el secundario no debe ocultarlo.
      console.warn(
        "[device-registry] Falló la restauración del perfil local tras error remoto",
        restoreError instanceof Error ? restoreError.message : "restore failed",
      );
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

/** @internal Expuesto para pruebas de restauración. */
export async function restorePreviousProfileForTests(
  previous: LocalDeviceProfile,
): Promise<void> {
  await restoreLocalDeviceProfile(previous);
}

/** Solo pruebas: limpia promesas en vuelo. */
export function resetDeviceRegistryForTests(): void {
  registerInflight.clear();
}
