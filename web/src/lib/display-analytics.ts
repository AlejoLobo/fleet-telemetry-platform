import {
  computeGlobalAnalytics,
  type FleetDataSource,
  type GlobalAnalytics,
  type SelectedVehicleAnalytics,
} from "@/lib/analytics";
import type { FleetAlert, VehicleStatus } from "@/types/fleet";

/** KPI globales alineados con el snapshot visual (mapa/lista/alertas). */
export function buildDisplayGlobalAnalytics(options: {
  dataSource: FleetDataSource | null;
  displayVehicles: VehicleStatus[];
  displayAlerts: FleetAlert[];
  fleetTruncated: boolean;
  globalAnalytics: GlobalAnalytics;
}): GlobalAnalytics {
  const {
    dataSource,
    displayVehicles,
    displayAlerts,
    fleetTruncated,
    globalAnalytics,
  } = options;

  if (dataSource === "demo") {
    return computeGlobalAnalytics(displayVehicles, displayAlerts, "demo");
  }

  // Snapshot truncado u Ops: conservar totales backend; actualizar solo alertas visibles.
  if (fleetTruncated || globalAnalytics.aggregationSource === "ops") {
    return {
      ...globalAnalytics,
      openAlerts: displayAlerts.length,
    };
  }

  if (dataSource === "api") {
    return computeGlobalAnalytics(displayVehicles, displayAlerts, "api");
  }

  return globalAnalytics;
}

/** Alinea nombre/estado del KPI seleccionado con el vehículo visible. */
export function buildDisplaySelectedAnalytics(
  selectedAnalytics: SelectedVehicleAnalytics | null,
  selectedVehicle: VehicleStatus | null,
): SelectedVehicleAnalytics | null {
  if (!selectedAnalytics) return null;
  if (!selectedVehicle) return selectedAnalytics;
  return {
    ...selectedAnalytics,
    vehicleName: selectedVehicle.vehicleName || selectedAnalytics.vehicleName,
  };
}
