import type { NormalizedVehiclePatch, VehicleStatus } from "@/types/fleet";
import { pickNewerVehiclePatch, toVehiclePatch } from "@/lib/fleet-merge";

/** Conserva la actualización más reciente por DeviceId con merge de identidad. */
export function bufferPendingVehicleUpdates(
  pending: Map<string, NormalizedVehiclePatch>,
  updates: ReadonlyArray<VehicleStatus | NormalizedVehiclePatch>,
): void {
  for (const update of updates) {
    const patch = toVehiclePatch(update);
    if (!patch.vehicle.deviceId) continue;
    const existing = pending.get(patch.vehicle.deviceId);
    pending.set(
      patch.vehicle.deviceId,
      existing ? pickNewerVehiclePatch(existing, patch) : patch,
    );
  }
}

/** Extrae y vacía el buffer como VehicleStatus (metadatos de presencia ya aplicados al fusionar). */
export function takePendingVehicleUpdates(
  pending: Map<string, NormalizedVehiclePatch>,
): NormalizedVehiclePatch[] {
  if (pending.size === 0) return [];
  const values = [...pending.values()];
  pending.clear();
  return values;
}
