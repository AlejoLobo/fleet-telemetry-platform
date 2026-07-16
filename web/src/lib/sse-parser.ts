import type { FleetAlert, NormalizedVehiclePatch, VehicleStatus } from "@/types/fleet";
import {
  normalizeVehicle,
  normalizeVehiclePatch,
  normalizeVehiclePatches,
  normalizeVehicles,
  type RawVehicle,
} from "@/lib/fleet-normalize";

export function parseFleetUpdatePayload(raw: string): VehicleStatus[] | null {
  try {
    const parsed = JSON.parse(raw) as VehicleStatus[] | RawVehicle;
    if (Array.isArray(parsed)) {
      return normalizeVehicles(parsed as RawVehicle[]);
    }
    const vehicle = normalizeVehicle(parsed as RawVehicle);
    return vehicle.deviceId ? [vehicle] : null;
  } catch (error) {
    if (process.env.NODE_ENV === "development") {
      console.warn("[SSE] Payload fleet-update inválido:", error);
    }
    return null;
  }
}

/** Parche individual SSE: conserva presencia de vehicleType para el merge. */
export function parseVehicleUpdatePayload(raw: string): NormalizedVehiclePatch | null {
  try {
    const parsed = JSON.parse(raw) as RawVehicle;
    const patch = normalizeVehiclePatch(parsed);
    return patch.vehicle.deviceId ? patch : null;
  } catch (error) {
    if (process.env.NODE_ENV === "development") {
      console.warn("[SSE] Payload vehicle-update inválido:", error);
    }
    return null;
  }
}

export function parseFleetUpdateAsPatches(raw: string): NormalizedVehiclePatch[] | null {
  try {
    const parsed = JSON.parse(raw) as RawVehicle[] | RawVehicle;
    if (Array.isArray(parsed)) {
      return normalizeVehiclePatches(parsed);
    }
    const patch = normalizeVehiclePatch(parsed);
    return patch.vehicle.deviceId ? [patch] : null;
  } catch {
    return null;
  }
}

export function parseAlertPayload(raw: string): FleetAlert | null {
  try {
    return JSON.parse(raw) as FleetAlert;
  } catch (error) {
    if (process.env.NODE_ENV === "development") {
      console.warn("[SSE] Payload alert inválido:", error);
    }
    return null;
  }
}
