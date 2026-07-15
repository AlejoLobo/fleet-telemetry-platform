import type { FleetAlert, VehicleStatus } from "@/types/fleet";
import { normalizeVehicle, normalizeVehicles } from "@/lib/fleet-normalize";

type RawVehicle = Parameters<typeof normalizeVehicle>[0];

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

export function parseVehicleUpdatePayload(raw: string): VehicleStatus | null {
  try {
    const parsed = JSON.parse(raw) as RawVehicle;
    const vehicle = normalizeVehicle(parsed);
    return vehicle.deviceId ? vehicle : null;
  } catch (error) {
    if (process.env.NODE_ENV === "development") {
      console.warn("[SSE] Payload vehicle-update inválido:", error);
    }
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
