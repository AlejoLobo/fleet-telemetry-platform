import * as SecureStore from "expo-secure-store";
import { generateEventId } from "@/utils/id";
import { getDefaultDriverId, getDefaultVehicleId } from "@/config/env";

const DEVICE_ID_KEY = "fleet.device.id";
const VEHICLE_NAME_KEY = "fleet.profile.vehicleName";
const DRIVER_NAME_KEY = "fleet.profile.driverName";

export type DriverProfile = {
  deviceId: string;
  vehicleName: string;
  driverName: string;
};

async function readOrCreateDeviceId(): Promise<string> {
  const existing = await SecureStore.getItemAsync(DEVICE_ID_KEY);
  if (existing) return existing;
  const created = await generateEventId();
  await SecureStore.setItemAsync(DEVICE_ID_KEY, created);
  return created;
}

export async function loadDriverProfile(): Promise<DriverProfile> {
  const deviceId = await readOrCreateDeviceId();
  const vehicleName =
    (await SecureStore.getItemAsync(VEHICLE_NAME_KEY))?.trim() || getDefaultVehicleId();
  const driverName =
    (await SecureStore.getItemAsync(DRIVER_NAME_KEY))?.trim() || getDefaultDriverId();
  return { deviceId, vehicleName, driverName };
}

export async function saveDriverProfile(profile: {
  vehicleName: string;
  driverName: string;
}): Promise<DriverProfile> {
  const deviceId = await readOrCreateDeviceId();
  const vehicleName = profile.vehicleName.trim() || getDefaultVehicleId();
  const driverName = profile.driverName.trim() || getDefaultDriverId();
  await SecureStore.setItemAsync(VEHICLE_NAME_KEY, vehicleName);
  await SecureStore.setItemAsync(DRIVER_NAME_KEY, driverName);
  return { deviceId, vehicleName, driverName };
}

/** Solo pruebas: limpia identidad de dispositivo y perfil. */
export async function resetDriverProfileForTests(): Promise<void> {
  await SecureStore.deleteItemAsync(DEVICE_ID_KEY);
  await SecureStore.deleteItemAsync(VEHICLE_NAME_KEY);
  await SecureStore.deleteItemAsync(DRIVER_NAME_KEY);
}
