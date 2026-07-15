import type { VehicleStatus } from "@/types/fleet";

/** Conserva solo la actualización más reciente por DeviceId. */
export function bufferPendingVehicleUpdates(
  pending: Map<string, VehicleStatus>,
  updates: readonly VehicleStatus[],
): void {
  for (const update of updates) {
    if (!update.deviceId) continue;
    pending.set(update.deviceId, update);
  }
}

/** Extrae y vacía el buffer. */
export function takePendingVehicleUpdates(
  pending: Map<string, VehicleStatus>,
): VehicleStatus[] {
  if (pending.size === 0) return [];
  const values = [...pending.values()];
  pending.clear();
  return values;
}
