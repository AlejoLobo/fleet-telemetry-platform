import type { FleetAlert, VehicleStatus } from "@/types/fleet";
import { normalizeVehicles } from "@/lib/fleet-normalize";

export function parseFleetUpdatePayload(raw: string): VehicleStatus[] | null {
  try {
    const parsed = JSON.parse(raw) as VehicleStatus[];
    return normalizeVehicles(parsed);
  } catch (error) {
    if (process.env.NODE_ENV === "development") {
      console.warn("[SSE] Payload fleet-update inválido:", error);
    }
    return null;
  }
}

export function parseVehicleUpdatePayload(raw: string): VehicleStatus | null {
  const vehicles = parseFleetUpdatePayload(raw);
  return vehicles && vehicles.length > 0 ? vehicles[0] : null;
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
