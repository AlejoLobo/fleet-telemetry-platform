import { describe, expect, it } from "vitest";
import { computeGlobalAnalytics, computeSelectedAnalytics } from "@/lib/analytics";
import type { FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";

const vehicle = (id: string, status: string): VehicleStatus => ({
  vehicleId: id,
  name: id,
  status,
  lastSeenAt: "2026-07-10T10:00:00Z",
  lastSpeedKmh: 40,
  lastLatitude: 4.65,
  lastLongitude: -74.08,
});

describe("analytics", () => {
  it("calcula KPI globales de flota", () => {
    const vehicles = [vehicle("VH-001", "online"), vehicle("VH-002", "offline")];
    const alerts: FleetAlert[] = [{ alertId: "1", vehicleId: "VH-001", alertType: "x", severity: "low", message: "", createdAt: "", isAcknowledged: false }];
    const global = computeGlobalAnalytics(vehicles, alerts, "api");
    expect(global.activeVehicles).toBe(1);
    expect(global.totalVehicles).toBe(2);
    expect(global.openAlerts).toBe(1);
  });

  it("calcula velocidad promedio del vehículo seleccionado", () => {
    const telemetry: TelemetryEvent[] = [
      { eventId: "1", vehicleId: "VH-001", driverId: null, timestamp: "", latitude: 0, longitude: 0, speedKmh: 60, fuelLevelPercent: null, batteryPercent: null },
      { eventId: "2", vehicleId: "VH-001", driverId: null, timestamp: "", latitude: 0, longitude: 0, speedKmh: 80, fuelLevelPercent: null, batteryPercent: null },
    ];
    const selected = computeSelectedAnalytics("VH-001", telemetry);
    expect(selected.averageSpeedKmh).toBe(70);
    expect(selected.eventCount).toBe(2);
  });
});
