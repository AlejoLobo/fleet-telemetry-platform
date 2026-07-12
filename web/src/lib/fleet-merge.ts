import type { VehicleStatus } from "@/types/fleet";

/** Fusiona actualizaciones SSE por VehicleId sin perder el snapshot base. */
export function mergeVehicleUpdates(
  snapshot: VehicleStatus[],
  updates: VehicleStatus[],
): VehicleStatus[] {
  if (updates.length === 0) return snapshot;

  const byId = new Map(snapshot.map((vehicle) => [vehicle.vehicleId, vehicle]));
  for (const update of updates) {
    if (!update.vehicleId) continue;
    byId.set(update.vehicleId, update);
  }

  return Array.from(byId.values());
}
