import type { VehicleStatus } from "@/types/fleet";

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** True si el valor es un nombre usable (no vacío, no ID de dispositivo, no UUID). */
export function isMeaningfulVehicleName(
  name: string | null | undefined,
  vehicleId: string,
): boolean {
  const trimmed = name?.trim();
  if (!trimmed) return false;
  if (trimmed.toLowerCase() === vehicleId.trim().toLowerCase()) return false;
  // Un UUID nunca es nombre de vehículo en el monitor.
  if (UUID_RE.test(trimmed)) return false;
  return true;
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
