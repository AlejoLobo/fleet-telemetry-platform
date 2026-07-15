/** Normaliza respuestas del backend al formato del frontend. */
import type { VehicleStatus } from "@/types/fleet";

type RawVehicle = Partial<VehicleStatus> & {
  VehicleId?: string;
  Name?: string;
  DriverId?: string | null;
  Status?: string;
  LastSeenAt?: string | null;
  LastEventId?: string | null;
  StatusEvaluatedAt?: string | null;
  LastSpeedKmh?: number | null;
  LastLatitude?: number | null;
  LastLongitude?: number | null;
  lastHeadingDegrees?: number | null;
  LastHeadingDegrees?: number | null;
  LastLocationSource?: string | null;
};

/** Normaliza un vehículo del API (PascalCase → camelCase). */
export function normalizeVehicle(vehicle: RawVehicle): VehicleStatus {
  const vehicleId = vehicle.vehicleId ?? vehicle.VehicleId ?? "";
  const rawName = (vehicle.name ?? vehicle.Name ?? "").trim();
  // Nunca promover UUID/device ID a name.
  const name =
    rawName &&
    rawName.toLowerCase() !== vehicleId.toLowerCase() &&
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(rawName)
      ? rawName
      : "";
  return {
    vehicleId,
    name,
    driverId: vehicle.driverId ?? vehicle.DriverId ?? null,
    status: vehicle.status ?? vehicle.Status ?? "offline",
    lastSeenAt: vehicle.lastSeenAt ?? vehicle.LastSeenAt ?? null,
    lastEventId: vehicle.lastEventId ?? vehicle.LastEventId ?? null,
    statusEvaluatedAt: vehicle.statusEvaluatedAt ?? vehicle.StatusEvaluatedAt ?? null,
    lastSpeedKmh: vehicle.lastSpeedKmh ?? vehicle.LastSpeedKmh ?? null,
    lastLatitude: vehicle.lastLatitude ?? vehicle.LastLatitude ?? null,
    lastLongitude: vehicle.lastLongitude ?? vehicle.LastLongitude ?? null,
    headingDegrees:
      vehicle.headingDegrees ?? vehicle.lastHeadingDegrees ?? vehicle.LastHeadingDegrees ?? null,
    lastLocationSource:
      vehicle.lastLocationSource ?? vehicle.LastLocationSource ?? null,
  };
}

export function normalizeVehicles(vehicles: RawVehicle[]): VehicleStatus[] {
  return vehicles.map(normalizeVehicle);
}
