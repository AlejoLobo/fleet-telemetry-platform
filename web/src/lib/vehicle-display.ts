import type { VehicleStatus } from "@/types/fleet";

/** Nombre visible: nunca usa el ID de dispositivo como título. */
export function getVehicleDisplayName(vehicle: Pick<VehicleStatus, "name" | "vehicleId">): string {
  const name = vehicle.name?.trim();
  if (!name || name === vehicle.vehicleId) return "Sin nombre";
  return name;
}
