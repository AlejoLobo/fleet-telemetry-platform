import { describe, expect, it } from "vitest";
import type { VehicleStatus } from "@/types/fleet";
import {
  formatFleetStatusCard,
  formatMetricsLine,
  formatStatusBadge,
  formatVehicleTooltip,
} from "@/lib/vehicle-display-format";

function vehicle(overrides: Partial<VehicleStatus> = {}): VehicleStatus {
  return {
    deviceId: "00000000-0000-4000-8000-000000000001",
    vehicleName: "VH-001",
    vehicleType: "truck",
    status: "online",
    lastSeenAt: "2026-07-10T15:04:05Z",
    lastSpeedKmh: 42,
    lastLatitude: 4.711,
    lastLongitude: -74.0721,
    driverId: "DRV-001",
    ...overrides,
  };
}

describe("vehicle-display-format", () => {
  it("card Estado de flota separa nombre y badge de estado", () => {
    const card = formatFleetStatusCard(vehicle());
    expect(card.name).toBe("VH-001");
    expect(card.status).toEqual({ label: "En línea", online: true });
    expect(card.deviceId).toBe("00000000-0000-4000-8000-000000000001");
    expect(card.metrics).toMatch(/^42 km\/h {2}.+ {2}Camión$/);
  });

  it("tooltip incluye conductor, coordenadas y badge", () => {
    const tip = formatVehicleTooltip(vehicle());
    expect(tip.name).toBe("VH-001");
    expect(tip.status).toEqual({ label: "En línea", online: true });
    expect(tip.deviceId).toBe("00000000-0000-4000-8000-000000000001");
    expect(tip.driverName).toBe("DRV-001");
    expect(tip.metrics).toContain("Camión");
    expect(tip.coordinates).toBe("4.71100, -74.07210");
  });

  it("Desconectado usa badge offline", () => {
    expect(formatStatusBadge("offline")).toEqual({ label: "Desconectado", online: false });
  });

  it("conductor ausente y velocidad null usan guión", () => {
    const tip = formatVehicleTooltip(
      vehicle({ driverId: null, lastSpeedKmh: null, lastLatitude: null, lastLongitude: null }),
    );
    expect(tip.driverName).toBe("—");
    expect(tip.metrics.startsWith("—")).toBe(true);
    expect(tip.coordinates).toBe("—");
  });

  it("velocidad cero no se confunde con null", () => {
    expect(formatMetricsLine(vehicle({ lastSpeedKmh: 0 }))).toMatch(/^0 km\/h /);
  });
});
