import { describe, expect, it } from "vitest";
import {
  computeGlobalAnalytics,
  computeGlobalAnalyticsFromOps,
  computeSelectedAnalytics,
} from "@/lib/analytics";
import type { FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";

const vehicle = (id: string, status: string): VehicleStatus => ({
  deviceId: id,
  vehicleName: id,
  status,
  lastSeenAt: "2026-07-10T10:00:00Z",
  lastSpeedKmh: 40,
  lastLatitude: 4.65,
  lastLongitude: -74.08,
});

describe("analytics", () => {
  it("calcula KPI globales de flota", () => {
    const vehicles = [vehicle("00000000-0000-4000-8000-000000000001", "online"), vehicle("00000000-0000-4000-8000-000000000002", "offline")];
    const alerts: FleetAlert[] = [
      {
        alertId: "1",
        deviceId: "00000000-0000-4000-8000-000000000001",
        alertType: "x",
        severity: "low",
        message: "",
        createdAt: "",
        isAcknowledged: false,
      },
    ];
    const global = computeGlobalAnalytics(vehicles, alerts, "api");
    expect(global.activeVehicles).toBe(1);
    expect(global.totalVehicles).toBe(2);
    expect(global.openAlerts).toBe(1);
    expect(global.aggregationSource).toBe("snapshot");
  });

  it("calcula velocidad promedio del vehículo seleccionado", () => {
    const telemetry: TelemetryEvent[] = [
      {
        eventId: "1",
        deviceId: "00000000-0000-4000-8000-000000000001",
        driverId: null,
        timestamp: "",
        latitude: 0,
        longitude: 0,
        speedKmh: 60,
        fuelLevelPercent: null,
        batteryPercent: null,
      },
      {
        eventId: "2",
        deviceId: "00000000-0000-4000-8000-000000000001",
        driverId: null,
        timestamp: "",
        latitude: 0,
        longitude: 0,
        speedKmh: 80,
        fuelLevelPercent: null,
        batteryPercent: null,
      },
    ];
    const selected = computeSelectedAnalytics("00000000-0000-4000-8000-000000000001", telemetry);
    expect(selected.averageSpeedKmh).toBe(70);
    expect(selected.eventCount).toBe(2);
  });
});

describe("analytics Ops vs snapshot", () => {
  it("Alertas_abiertas_no_usan_criticalAlerts", () => {
    const openAlertsFromApi = 7;
    const global = computeGlobalAnalyticsFromOps(
      { totalVehicles: 100, activeVehicles: 80 },
      openAlertsFromApi,
      "api",
      { partial: true },
    );

    expect(global.openAlerts).toBe(7);
    expect(global.openAlerts).not.toBe(12);
  });

  it("Ops_success_usa_total_y_active_globales", () => {
    const global = computeGlobalAnalyticsFromOps(
      { totalVehicles: 12000, activeVehicles: 8500 },
      4,
      "api",
      { partial: true },
    );

    expect(global.totalVehicles).toBe(12000);
    expect(global.activeVehicles).toBe(8500);
    expect(global.aggregationSource).toBe("ops");
    expect(global.partial).toBe(true);
  });

  it("Ops_failure_marca_metricas_como_snapshot_parcial", () => {
    const vehicles = [vehicle("00000000-0000-4000-8000-000000000001", "online"), vehicle("00000000-0000-4000-8000-000000000002", "online")];
    const alerts: FleetAlert[] = [
      {
        alertId: "a1",
        deviceId: "00000000-0000-4000-8000-000000000001",
        alertType: "overspeed",
        severity: "critical",
        message: "x",
        createdAt: "2026-07-10T10:00:00Z",
        isAcknowledged: false,
      },
    ];

    const global = computeGlobalAnalytics(vehicles, alerts, "api", {
      partial: true,
      aggregationSource: "snapshot",
    });

    expect(global.totalVehicles).toBe(2);
    expect(global.activeVehicles).toBe(2);
    expect(global.openAlerts).toBe(1);
    expect(global.aggregationSource).toBe("snapshot");
    expect(global.partial).toBe(true);
  });

  it("Analitica_no_presenta_5000_como_total_si_hay_mas", () => {
    const analytics = computeGlobalAnalyticsFromOps(
      { totalVehicles: 12000, activeVehicles: 8000 },
      5,
      "api",
      { partial: true },
    );

    expect(analytics.totalVehicles).toBe(12000);
    expect(analytics.activeVehicles).toBe(8000);
    expect(analytics.partial).toBe(true);
  });
});
