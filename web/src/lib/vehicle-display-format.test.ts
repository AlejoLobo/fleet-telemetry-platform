import { describe, expect, it } from "vitest";
import type { VehicleStatus } from "@/types/fleet";
import {
  formatFleetStatusCard,
  formatMetricsLine,
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
  it("card Estado de flota sigue el formato de 3 líneas", () => {
    const card = formatFleetStatusCard(vehicle());
    expect(card.title).toBe("VH-001 (En línea)");
    expect(card.deviceId).toBe("00000000-0000-4000-8000-000000000001");
    expect(card.metrics).toMatch(/^42 km\/h {2}.+ {2}Camión$/);
  });

  it("tooltip incluye conductor y coordenadas", () => {
    const tip = formatVehicleTooltip(vehicle());
    expect(tip.title).toBe("VH-001 (En línea)");
    expect(tip.deviceId).toBe("00000000-0000-4000-8000-000000000001");
    expect(tip.driverName).toBe("DRV-001");
    expect(tip.metrics).toContain("Camión");
    expect(tip.coordinates).toBe("4.71100, -74.07210");
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
