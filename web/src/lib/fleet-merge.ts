import type { VehicleStatus } from "@/types/fleet";
import { resolveVehicleName } from "@/lib/vehicle-display";

function parseLastSeenAt(value: string | null | undefined): number | null {
  if (!value) return null;
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

function parseEventId(value: string | null | undefined): string | null {
  if (!value) return null;
  const normalized = value.trim().toLowerCase();
  return normalized.length > 0 ? normalized : null;
}

function parseStatusEvaluatedAt(value: string | null | undefined): number | null {
  if (!value) return null;
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

function compareEventId(left: string | null, right: string | null): number {
  if (left === null && right === null) return 0;
  if (left === null) return -1;
  if (right === null) return 1;
  return left.localeCompare(right);
}

function compareVehicleRecency(left: VehicleStatus, right: VehicleStatus): number {
  const leftMs = parseLastSeenAt(left.lastSeenAt);
  const rightMs = parseLastSeenAt(right.lastSeenAt);

  if (leftMs === null && rightMs === null) {
    const eventCompare = compareEventId(parseEventId(left.lastEventId), parseEventId(right.lastEventId));
    if (eventCompare !== 0) return eventCompare;
    const leftEval = parseStatusEvaluatedAt(left.statusEvaluatedAt);
    const rightEval = parseStatusEvaluatedAt(right.statusEvaluatedAt);
    if (leftEval === null && rightEval === null) return 0;
    if (leftEval === null) return -1;
    if (rightEval === null) return 1;
    return leftEval - rightEval;
  }
  if (leftMs === null) return -1;
  if (rightMs === null) return 1;
  if (leftMs !== rightMs) return leftMs - rightMs;

  const eventCompare = compareEventId(parseEventId(left.lastEventId), parseEventId(right.lastEventId));
  if (eventCompare !== 0) return eventCompare;

  const leftEval = parseStatusEvaluatedAt(left.statusEvaluatedAt);
  const rightEval = parseStatusEvaluatedAt(right.statusEvaluatedAt);
  if (leftEval === null && rightEval === null) return 0;
  if (leftEval === null) return -1;
  if (rightEval === null) return 1;
  return leftEval - rightEval;
}

function pickNewerVehicle(current: VehicleStatus, incoming: VehicleStatus): VehicleStatus {
  const newer = compareVehicleRecency(incoming, current) >= 0 ? incoming : current;
  const older = newer === incoming ? current : incoming;
  // SSE a veces manda Name=VehicleId; no debe borrar el nombre operativo ya conocido.
  return {
    ...newer,
    name: resolveVehicleName(newer.name, older.name) || newer.name || older.name || "",
    deviceId: newer.deviceId ?? older.deviceId,
    vehicleType: newer.vehicleType ?? older.vehicleType,
    driverId: newer.driverId ?? older.driverId,
  };
}

/** Fusiona actualizaciones por VehicleId conservando el registro más reciente. */
export function mergeVehicleUpdates(
  snapshot: VehicleStatus[],
  updates: VehicleStatus[],
): VehicleStatus[] {
  if (updates.length === 0) return snapshot;

  const patchById = new Map<string, VehicleStatus>();
  for (const update of updates) {
    if (!update.vehicleId) continue;
    const existing = patchById.get(update.vehicleId);
    patchById.set(update.vehicleId, existing ? pickNewerVehicle(existing, update) : update);
  }

  const merged: VehicleStatus[] = [];
  const seen = new Set<string>();

  for (const vehicle of snapshot) {
    const patch = patchById.get(vehicle.vehicleId);
    merged.push(patch ? pickNewerVehicle(vehicle, patch) : vehicle);
    seen.add(vehicle.vehicleId);
    patchById.delete(vehicle.vehicleId);
  }

  for (const patch of patchById.values()) {
    if (!seen.has(patch.vehicleId)) {
      merged.push(patch);
      seen.add(patch.vehicleId);
    }
  }

  return merged;
}

/** Elimina parches obsoletos cuando el snapshot ya contiene un estado igual o más reciente. */
export function pruneVehiclePatches(
  patches: VehicleStatus[],
  snapshot: VehicleStatus[],
): VehicleStatus[] {
  if (patches.length === 0 || snapshot.length === 0) return patches;

  const snapshotById = new Map(snapshot.map((vehicle) => [vehicle.vehicleId, vehicle]));
  return patches.filter((patch) => {
    const snapshotVehicle = snapshotById.get(patch.vehicleId);
    if (!snapshotVehicle) return true;
    return compareVehicleRecency(patch, snapshotVehicle) > 0;
  });
}

export {
  compareVehicleRecency,
  parseLastSeenAt,
  parseEventId,
  parseStatusEvaluatedAt,
  compareEventId,
  pickNewerVehicle,
};
