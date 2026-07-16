/** Catálogo cerrado de tipos de vehículo (códigos canónicos en inglés). */

export const VEHICLE_TYPES = [
  "car",
  "motorcycle",
  "van",
  "truck",
  "bus",
  "pickup",
] as const;

export type VehicleType = (typeof VEHICLE_TYPES)[number];

export const VEHICLE_TYPE_LABELS: Record<VehicleType, string> = {
  car: "Automóvil",
  motorcycle: "Motocicleta",
  van: "Van",
  truck: "Camión",
  bus: "Bus",
  pickup: "Camioneta",
};

export const DEFAULT_VEHICLE_TYPE: VehicleType = "car";

export function isVehicleType(value: unknown): value is VehicleType {
  return typeof value === "string" && (VEHICLE_TYPES as readonly string[]).includes(value);
}

/** Normaliza entrada case-insensitive; inválido o ausente → car. */
export function normalizeVehicleType(value: unknown): VehicleType {
  if (typeof value !== "string") return DEFAULT_VEHICLE_TYPE;
  const normalized = value.trim().toLowerCase();
  return isVehicleType(normalized) ? normalized : DEFAULT_VEHICLE_TYPE;
}

export function vehicleTypeLabel(type: VehicleType): string {
  return VEHICLE_TYPE_LABELS[type];
}
