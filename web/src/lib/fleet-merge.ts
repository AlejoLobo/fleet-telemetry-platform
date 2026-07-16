import type { NormalizedVehiclePatch, VehicleStatus } from "@/types/fleet";

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

function isNormalizedPatch(
  value: VehicleStatus | NormalizedVehiclePatch,
): value is NormalizedVehiclePatch {
  return (
    typeof value === "object"
    && value !== null
    && "vehicle" in value
    && "hasVehicleType" in value
    && typeof (value as NormalizedVehiclePatch).hasVehicleType === "boolean"
  );
}

/** Adapta VehicleStatus o NormalizedVehiclePatch a parche tipado. */
export function toVehiclePatch(
  value: VehicleStatus | NormalizedVehiclePatch,
  hasVehicleTypeDefault = true,
): NormalizedVehiclePatch {
  if (isNormalizedPatch(value)) return value;
  return { vehicle: value, hasVehicleType: hasVehicleTypeDefault };
}

/** Fusiona identidad del vehículo conservando telemetría del registro más reciente. */
function mergeVehicleIdentity(
  base: VehicleStatus,
  patch: NormalizedVehiclePatch,
  newer: VehicleStatus,
): VehicleStatus {
  const patchName = patch.vehicle.vehicleName?.trim() ?? "";
  const vehicleName = patchName.length > 0 ? patch.vehicle.vehicleName : base.vehicleName;
  const vehicleType = patch.hasVehicleType ? patch.vehicle.vehicleType : base.vehicleType;

  return {
    ...newer,
    deviceId: base.deviceId,
    vehicleName,
    vehicleType,
  };
}

function pickNewerVehiclePatch(
  base: NormalizedVehiclePatch,
  patch: NormalizedVehiclePatch,
): NormalizedVehiclePatch {
  const newerVehicle =
    compareVehicleRecency(patch.vehicle, base.vehicle) >= 0 ? patch.vehicle : base.vehicle;
  const preferredPatch =
    compareVehicleRecency(patch.vehicle, base.vehicle) >= 0 ? patch : base;
  const other = preferredPatch === patch ? base : patch;

  const mergedVehicle = mergeVehicleIdentity(other.vehicle, preferredPatch, newerVehicle);
  return {
    vehicle: mergedVehicle,
    hasVehicleType: preferredPatch.hasVehicleType || other.hasVehicleType,
  };
}

function pickNewerOntoSnapshot(
  base: VehicleStatus,
  patch: NormalizedVehiclePatch,
): VehicleStatus {
  const newer =
    compareVehicleRecency(patch.vehicle, base) >= 0 ? patch.vehicle : base;
  return mergeVehicleIdentity(base, patch, newer);
}

/**
 * Fusiona actualizaciones por DeviceId.
 * Acepta VehicleStatus (tratado como hasVehicleType=true) o NormalizedVehiclePatch.
 */
export function mergeVehicleUpdates(
  snapshot: VehicleStatus[],
  updates: Array<VehicleStatus | NormalizedVehiclePatch>,
): VehicleStatus[] {
  if (updates.length === 0) return snapshot;

  const patchById = new Map<string, NormalizedVehiclePatch>();
  for (const update of updates) {
    const patch = toVehiclePatch(update);
    if (!patch.vehicle.deviceId) continue;
    const existing = patchById.get(patch.vehicle.deviceId);
    patchById.set(
      patch.vehicle.deviceId,
      existing ? pickNewerVehiclePatch(existing, patch) : patch,
    );
  }

  const merged: VehicleStatus[] = [];
  const seen = new Set<string>();

  for (const vehicle of snapshot) {
    const patch = patchById.get(vehicle.deviceId);
    merged.push(patch ? pickNewerOntoSnapshot(vehicle, patch) : vehicle);
    seen.add(vehicle.deviceId);
    patchById.delete(vehicle.deviceId);
  }

  for (const patch of patchById.values()) {
    if (!seen.has(patch.vehicle.deviceId)) {
      merged.push(patch.vehicle);
      seen.add(patch.vehicle.deviceId);
    }
  }

  return merged;
}

/** Elimina parches obsoletos cuando el snapshot ya contiene un estado igual o más reciente. */
export function pruneVehiclePatches(
  patches: Array<VehicleStatus | NormalizedVehiclePatch>,
  snapshot: VehicleStatus[],
): VehicleStatus[] {
  if (patches.length === 0 || snapshot.length === 0) {
    return patches.map((p) => toVehiclePatch(p).vehicle);
  }

  const snapshotById = new Map(snapshot.map((vehicle) => [vehicle.deviceId, vehicle]));
  return patches
    .map((p) => toVehiclePatch(p))
    .filter((patch) => {
      const snapshotVehicle = snapshotById.get(patch.vehicle.deviceId);
      if (!snapshotVehicle) return true;
      return compareVehicleRecency(patch.vehicle, snapshotVehicle) > 0;
    })
    .map((patch) => patch.vehicle);
}

/** @deprecated Preferir pick via NormalizedVehiclePatch; conserva API de tests. */
function pickNewerVehicle(base: VehicleStatus, patch: VehicleStatus): VehicleStatus {
  return pickNewerOntoSnapshot(base, toVehiclePatch(patch));
}

export {
  compareVehicleRecency,
  parseLastSeenAt,
  parseEventId,
  parseStatusEvaluatedAt,
  compareEventId,
  pickNewerVehicle,
  mergeVehicleIdentity,
  pickNewerVehiclePatch,
  pickNewerOntoSnapshot,
};
