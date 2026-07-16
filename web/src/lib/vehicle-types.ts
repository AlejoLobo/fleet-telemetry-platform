import type { VehicleType } from "@/types/fleet";

/** Tipos de vehículo soportados en orden estable para selects y leyendas. */
export const VEHICLE_TYPES: readonly VehicleType[] = [
  "car",
  "motorcycle",
  "van",
  "truck",
  "bus",
  "pickup",
] as const;

const VEHICLE_TYPE_LABELS: Record<VehicleType, string> = {
  car: "Automóvil",
  motorcycle: "Motocicleta",
  van: "Van",
  truck: "Camión",
  bus: "Bus",
  pickup: "Camioneta",
};

export function isVehicleType(value: unknown): value is VehicleType {
  return typeof value === "string" && (VEHICLE_TYPES as readonly string[]).includes(value);
}

/** Devuelve el tipo canónico o null si ausente/inválido (sin default). */
export function parseVehicleType(value: unknown): VehicleType | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return isVehicleType(normalized) ? normalized : null;
}

/** Normaliza un valor crudo de tipo de vehículo; ausente o inválido → car. */
export function normalizeVehicleType(value: unknown): VehicleType {
  return parseVehicleType(value) ?? "car";
}

/** Etiqueta en español para un tipo de vehículo. */
export function vehicleTypeLabel(type: VehicleType): string {
  return VEHICLE_TYPE_LABELS[type];
}
