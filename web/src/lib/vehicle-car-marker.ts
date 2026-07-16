/** Compat: delega en createVehicleMarkerIcon del módulo unificado. */
import L from "leaflet";
import type { VehicleStatus } from "@/types/fleet";
import { createVehicleMarkerIcon } from "@/lib/vehicle-marker";

/** @deprecated Usar createVehicleMarkerIcon de vehicle-marker.ts */
export function createCarMarkerIcon(vehicle: VehicleStatus, selected: boolean): L.DivIcon {
  return createVehicleMarkerIcon(vehicle, selected);
}

export { createVehicleMarkerIcon } from "@/lib/vehicle-marker";
