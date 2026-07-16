/** @vitest-environment jsdom */
import { describe, expect, it } from "vitest";
import {
  buildDisplayGlobalAnalytics,
  buildDisplaySelectedAnalytics,
} from "@/lib/display-analytics";
import type { FleetAlert, VehicleStatus } from "@/types/fleet";
import type { GlobalAnalytics } from "@/lib/analytics";

function vehicle(id: string, status: "online" | "offline"): VehicleStatus {
  return {
    deviceId: id,
    vehicleName: `VH-${id.slice(-3)}`,
    vehicleType: "car",
    status,
    lastSeenAt: "2026-07-15T12:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74,
  };
}

const alert = (id: string): FleetAlert => ({
  alertId: id,
  deviceId: "d1",
  alertType: "overspeed",
  severity: "critical",
  message: "x",
  createdAt: "2026-07-15T12:00:00Z",
  isAcknowledged: false,
});

describe("buildDisplayGlobalAnalytics", () => {
  it("demo recalcula desde vehículos y alertas visibles", () => {
    const result = buildDisplayGlobalAnalytics({
      dataSource: "demo",
      displayVehicles: [vehicle("d1", "online"), vehicle("d2", "offline")],
      displayAlerts: [alert("a1")],
      fleetTruncated: false,
      globalAnalytics: {
        activeVehicles: 0,
        totalVehicles: 0,
        openAlerts: 0,
        source: "Demostración",
      },
    });
    expect(result.activeVehicles).toBe(1);
    expect(result.totalVehicles).toBe(2);
    expect(result.openAlerts).toBe(1);
  });

  it("api no truncado refleja SSE visual", () => {
    const result = buildDisplayGlobalAnalytics({
      dataSource: "api",
      displayVehicles: [vehicle("d1", "online"), vehicle("d2", "online")],
      displayAlerts: [alert("a1"), alert("a2")],
      fleetTruncated: false,
      globalAnalytics: {
        activeVehicles: 0,
        totalVehicles: 1,
        openAlerts: 0,
        source: "TimescaleDB",
        aggregationSource: "snapshot",
      },
    });
    expect(result.activeVehicles).toBe(2);
    expect(result.totalVehicles).toBe(2);
    expect(result.openAlerts).toBe(2);
  });

  it("snapshot truncado conserva totales backend y actualiza alertas", () => {
    const backend: GlobalAnalytics = {
      activeVehicles: 6000,
      totalVehicles: 9000,
      openAlerts: 1,
      source: "TimescaleDB",
      partial: true,
      aggregationSource: "ops",
    };
    const result = buildDisplayGlobalAnalytics({
      dataSource: "api",
      displayVehicles: [vehicle("d1", "online")],
      displayAlerts: [alert("a1"), alert("a2"), alert("a3")],
      fleetTruncated: true,
      globalAnalytics: backend,
    });
    expect(result.totalVehicles).toBe(9000);
    expect(result.activeVehicles).toBe(6000);
    expect(result.openAlerts).toBe(3);
    expect(result.aggregationSource).toBe("ops");
  });
});

describe("buildDisplaySelectedAnalytics", () => {
  it("alinea el nombre visible del vehículo SSE", () => {
    const selected = vehicle("d1", "online");
    const result = buildDisplaySelectedAnalytics(
      {
        deviceId: "d1",
        vehicleName: "Viejo",
        averageSpeedKmh: 30,
        eventCount: 5,
      },
      selected,
    );
    expect(result?.vehicleName).toBe(selected.vehicleName);
  });
});
