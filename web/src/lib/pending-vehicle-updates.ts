import type { VehicleStatus } from "@/types/fleet";
import { pickNewerVehicle } from "@/lib/fleet-merge";

/** Conserva la actualización más reciente por DeviceId con merge de identidad. */
export function bufferPendingVehicleUpdates(
  pending: Map<string, VehicleStatus>,
  updates: readonly VehicleStatus[],
): void {
  for (const update of updates) {
    if (!update.deviceId) continue;
    const existing = pending.get(update.deviceId);
    pending.set(update.deviceId, existing ? pickNewerVehicle(existing, update) : update);
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
