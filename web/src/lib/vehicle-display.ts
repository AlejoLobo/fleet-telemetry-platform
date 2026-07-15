import type { VehicleStatus } from "@/types/fleet";

/** True si el valor es un nombre usable (no vacío ni igual al ID de dispositivo). */
export function isMeaningfulVehicleName(
  name: string | null | undefined,
  vehicleId: string,
): boolean {
  const trimmed = name?.trim();
  return Boolean(trimmed && trimmed !== vehicleId);
}

/** Nombre usable o vacío; nunca cae al ID de dispositivo. */
export function resolveVehicleName(
  preferred: string | null | undefined,
  fallback: string | null | undefined,
  vehicleId: string,
): string {
  if (isMeaningfulVehicleName(preferred, vehicleId)) return preferred!.trim();
  if (isMeaningfulVehicleName(fallback, vehicleId)) return fallback!.trim();
  return "";
}

/** Nombre visible en UI: nunca usa el ID de dispositivo como título. */
export function getVehicleDisplayName(vehicle: Pick<VehicleStatus, "name" | "vehicleId">): string {
  const name = resolveVehicleName(vehicle.name, null, vehicle.vehicleId);
  return name || "Sin nombre";
}
