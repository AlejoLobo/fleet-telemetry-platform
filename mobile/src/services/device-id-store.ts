import * as SecureStore from "expo-secure-store";
import { generateEventId } from "@/utils/id";

const DEVICE_ID_KEY = "fleet.device.id";

/** ID estable del dispositivo físico; no cambia con vehículo/conductor. */
export async function loadOrCreateDeviceId(): Promise<string> {
  try {
    const existing = await SecureStore.getItemAsync(DEVICE_ID_KEY);
    if (existing && existing.trim().length > 0) {
      return existing.trim();
    }
  } catch {
    // Si SecureStore falla al leer, se genera uno de sesión más abajo.
  }

  const created = await generateEventId();
  try {
    await SecureStore.setItemAsync(DEVICE_ID_KEY, created);
  } catch {
    // Conserva el ID en memoria de proceso aunque no se pueda persistir.
  }
  return created;
}

/** Solo pruebas: borra el ID estable almacenado. */
export async function resetDeviceIdForTests(): Promise<void> {
  await SecureStore.deleteItemAsync(DEVICE_ID_KEY);
}
