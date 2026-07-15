/** Reglas de presentación de la flota en el monitor web. */
import type { VehicleStatus } from "@/types/fleet";
import type { FleetDataSource } from "@/lib/analytics";
import { mergeVehicleUpdates } from "@/lib/fleet-merge";
import { applyConnectivityFreshness } from "@/lib/vehicle-connectivity";
import { esVehiculoEnLinea } from "@/lib/labels";

type ResolveDisplayVehiclesParams = {
  vehicles: VehicleStatus[];
  livePatches: VehicleStatus[];
  dataSource: FleetDataSource;
  connectivityNowMs: number;
  /**
   * Tras pulsar Actualizar (liveOnly):
   * - se quitan desconectados que no estaban en el snapshot en vivo;
   * - si un vehículo del snapshot pasa a offline después, permanece visible.
   */
  afterLiveRefresh: boolean;
};

/** Calcula la lista visible de vehículos para mapa y estado de flota. */
export function resolveDisplayVehicles({
  vehicles,
  livePatches,
  dataSource,
  connectivityNowMs,
  afterLiveRefresh,
}: ResolveDisplayVehiclesParams): VehicleStatus[] {
  if (dataSource === "demo") {
    return vehicles;
  }

  const merged =
    livePatches.length === 0 ? vehicles : mergeVehicleUpdates(vehicles, livePatches);
  const freshened = applyConnectivityFreshness(merged, connectivityNowMs);

  if (!afterLiveRefresh) {
    return freshened;
  }

  const baseIds = new Set(vehicles.map((vehicle) => vehicle.vehicleId));
  // Conserva el snapshot post-Actualizar (aunque luego queden offline) y
  // permite vehículos nuevos realmente en línea vía SSE; no reintroduce offline antiguos.
  return freshened.filter(
    (vehicle) => baseIds.has(vehicle.vehicleId) || esVehiculoEnLinea(vehicle.status),
  );
}
