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

/** Normaliza un valor crudo de tipo de vehículo; ausente o inválido → car. */
export function normalizeVehicleType(value: unknown): VehicleType {
  if (typeof value !== "string") return "car";
  const normalized = value.trim().toLowerCase();
  if ((VEHICLE_TYPES as readonly string[]).includes(normalized)) {
    return normalized as VehicleType;
  }
  return "car";
}

/** Etiqueta en español para un tipo de vehículo. */
export function vehicleTypeLabel(type: VehicleType): string {
  return VEHICLE_TYPE_LABELS[type];
}
