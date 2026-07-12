/** Normaliza respuestas del backend al formato del frontend. */
import type { VehicleStatus } from "@/types/fleet";

type RawVehicle = VehicleStatus & {
  VehicleId?: string;
  Name?: string;
  Status?: string;
  LastSeenAt?: string | null;
  LastSpeedKmh?: number | null;
  LastLatitude?: number | null;
  LastLongitude?: number | null;
  lastHeadingDegrees?: number | null;
  LastHeadingDegrees?: number | null;
};

/** Normaliza un vehículo del API (PascalCase → camelCase). */
export function normalizeVehicle(vehicle: RawVehicle): VehicleStatus {
  return {
    vehicleId: vehicle.vehicleId ?? vehicle.VehicleId ?? "",
    name: vehicle.name ?? vehicle.Name ?? "",
    status: vehicle.status ?? vehicle.Status ?? "offline",
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
