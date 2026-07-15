import { describe, expect, it } from "vitest";
import { applyConnectivityFreshness, getOnlineThresholdMs } from "@/lib/vehicle-connectivity";
import type { VehicleStatus } from "@/types/fleet";

describe("vehicle-connectivity", () => {
  const base: VehicleStatus = {
    vehicleId: "VH-001",
    name: "VH-001",
    status: "online",
    lastSeenAt: "2026-07-15T00:00:00.000Z",
    lastLatitude: 4.65,
    lastLongitude: -74.08,
    lastSpeedKmh: 40,
    lastEventId: "11111111-1111-1111-1111-111111111111",
  };

  it("marca offline cuando lastSeenAt supera el umbral", () => {
    const now = Date.parse("2026-07-15T00:01:00.000Z");
    const result = applyConnectivityFreshness([base], now, 45_000);
    expect(result[0]?.status).toBe("offline");
  });

  it("conserva online dentro del umbral", () => {
    const now = Date.parse("2026-07-15T00:00:30.000Z");
    const result = applyConnectivityFreshness([base], now, 45_000);
    expect(result[0]?.status).toBe("online");
  });

  it("getOnlineThresholdMs usa valor por defecto positivo", () => {
    expect(getOnlineThresholdMs()).toBeGreaterThan(0);
  });
});
