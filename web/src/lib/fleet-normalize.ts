/** Normaliza respuestas del backend al formato del frontend. */
import type { VehicleStatus } from "@/types/fleet";

type RawVehicle = VehicleStatus & {
  lastHeadingDegrees?: number | null;
  LastLatitude?: number | null;
  LastLongitude?: number | null;
  LastHeadingDegrees?: number | null;
  LastSpeedKmh?: number | null;
  LastSeenAt?: string | null;
};

/** Normaliza un vehículo del API (PascalCase → camelCase). */
export function normalizeVehicle(vehicle: RawVehicle): VehicleStatus {
  return {
    vehicleId: vehicle.vehicleId,
    name: vehicle.name,
    status: vehicle.status,
    lastSeenAt: vehicle.lastSeenAt ?? vehicle.LastSeenAt ?? null,
    lastSpeedKmh: vehicle.lastSpeedKmh ?? vehicle.LastSpeedKmh ?? null,
    lastLatitude: vehicle.lastLatitude ?? vehicle.LastLatitude ?? null,
    lastLongitude: vehicle.lastLongitude ?? vehicle.LastLongitude ?? null,
    headingDegrees:
      vehicle.headingDegrees ?? vehicle.lastHeadingDegrees ?? vehicle.LastHeadingDegrees ?? null,
  };
}

export function normalizeVehicles(vehicles: RawVehicle[]): VehicleStatus[] {
  return vehicles.map(normalizeVehicle);
}
