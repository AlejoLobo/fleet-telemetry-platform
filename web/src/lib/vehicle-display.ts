import type { VehicleStatus } from "@/types/fleet";

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** True si el texto parece un UUID de dispositivo. */
export function isDeviceUuid(value: string | null | undefined): boolean {
  return Boolean(value && UUID_RE.test(value.trim()));
}

/**
 * Nombre operativo del vehículo (ej. VH-001).
 * Nunca usa un UUID de dispositivo como título.
 */
export function isMeaningfulVehicleName(name: string | null | undefined): boolean {
  const trimmed = name?.trim();
  if (!trimmed) return false;
  return !isDeviceUuid(trimmed);
}

export function resolveVehicleName(
  preferred: string | null | undefined,
  fallback: string | null | undefined,
): string {
  if (isMeaningfulVehicleName(preferred)) return preferred!.trim();
  if (isMeaningfulVehicleName(fallback)) return fallback!.trim();
  return "";
}

/** Título en lista/mapa: nombre del vehículo (VH-001), no el device ID. */
export function getVehicleDisplayName(
  vehicle: Pick<VehicleStatus, "name" | "vehicleId">,
): string {
  const fromName = resolveVehicleName(vehicle.name, null);
  if (fromName) return fromName;
  // Demo/legado: a veces el código de flota vive en vehicleId.
  if (isMeaningfulVehicleName(vehicle.vehicleId)) return vehicle.vehicleId.trim();
  return "Sin nombre";
}

/** ID de dispositivo mostrado en la segunda línea. */
export function getVehicleDeviceId(
  vehicle: Pick<VehicleStatus, "vehicleId" | "deviceId">,
): string {
  const explicit = vehicle.deviceId?.trim();
  if (explicit) return explicit;
  return vehicle.vehicleId;
}
