import type { AnalyticsSummary, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";

export type FleetDataSource = "api" | "demo";
export type AggregationSource = "snapshot" | "ops";

export type GlobalAnalytics = {
  activeVehicles: number;
  totalVehicles: number;
  openAlerts: number;
  source: string;
  partial?: boolean;
  aggregationSource?: AggregationSource;
};

export type OpsFleetCounts = {
  totalVehicles: number;
  activeVehicles: number;
};

export type SelectedVehicleAnalytics = {
  vehicleId: string;
  averageSpeedKmh: number;
  eventCount: number;
};

export function computeGlobalAnalytics(
  vehicles: VehicleStatus[],
  alerts: FleetAlert[],
  dataSource: FleetDataSource,
  options?: { partial?: boolean; aggregationSource?: AggregationSource },
): GlobalAnalytics {
  return {
    activeVehicles: vehicles.filter((v) => v.status === "online").length,
    totalVehicles: vehicles.length,
    openAlerts: alerts.length,
    source: dataSource === "demo" ? "Demostración" : "TimescaleDB",
    partial: options?.partial,
    aggregationSource: options?.aggregationSource ?? "snapshot",
  };
}

export function computeGlobalAnalyticsFromOps(
  summary: OpsFleetCounts,
  openAlerts: number,
  dataSource: FleetDataSource,
  options?: { partial?: boolean },
): GlobalAnalytics {
  return {
    activeVehicles: summary.activeVehicles,
    totalVehicles: summary.totalVehicles,
    openAlerts,
    source: dataSource === "demo" ? "Demostración" : "TimescaleDB",
    partial: options?.partial,
    aggregationSource: "ops",
  };
}

export function computeSelectedAnalytics(
  vehicleId: string,
  telemetry: TelemetryEvent[],
): SelectedVehicleAnalytics {
  const speeds = telemetry.map((t) => t.speedKmh);
  const avg = speeds.length ? speeds.reduce((a, b) => a + b, 0) / speeds.length : 0;

  return {
    vehicleId,
    averageSpeedKmh: Math.round(avg * 10) / 10,
    eventCount: telemetry.length,
  };
}

/** Compatibilidad con componentes existentes que esperan AnalyticsSummary. */
export function toLegacyAnalytics(
  global: GlobalAnalytics,
  selected: SelectedVehicleAnalytics | null,
): AnalyticsSummary {
  return {
    averageSpeedKmh: selected?.averageSpeedKmh ?? 0,
    activeVehicles: global.activeVehicles,
    totalVehicles: global.totalVehicles,
    openAlerts: global.openAlerts,
    source: global.source,
  };
}
