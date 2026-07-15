import { describe, expect, it } from "vitest";
import { resolveDisplayVehicles } from "@/lib/fleet-display";
import type { VehicleStatus } from "@/types/fleet";

function vehicle(
  id: string,
  overrides: Partial<VehicleStatus> = {},
): VehicleStatus {
  return {
    vehicleId: id,
    name: id,
    status: "online",
    lastSeenAt: "2026-07-15T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
    ...overrides,
  };
}

describe("resolveDisplayVehicles", () => {
  const now = Date.parse("2026-07-15T10:00:30Z");

  it("sin Actualizar conserva desconectados", () => {
    const display = resolveDisplayVehicles({
      vehicles: [
        vehicle("VH-ON"),
        vehicle("VH-OFF", {
          status: "offline",
          lastSeenAt: "2026-07-15T09:00:00Z",
        }),
      ],
      livePatches: [],
      dataSource: "api",
      connectivityNowMs: now,
      afterLiveRefresh: false,
    });

    expect(display.map((v) => v.vehicleId).sort()).toEqual(["VH-OFF", "VH-ON"]);
  });

  it("tras Actualizar no reintroduce offline antiguos por SSE", () => {
    const display = resolveDisplayVehicles({
      vehicles: [vehicle("VH-ON")],
      livePatches: [
        vehicle("VH-STALE", {
          status: "offline",
          lastSeenAt: "2026-07-15T09:00:00Z",
        }),
      ],
      dataSource: "api",
      connectivityNowMs: now,
      afterLiveRefresh: true,
    });

    expect(display.map((v) => v.vehicleId)).toEqual(["VH-ON"]);
  });

  it("tras Actualizar mantiene vehículos del snapshot que pasan a offline", () => {
    const display = resolveDisplayVehicles({
      vehicles: [
        vehicle("VH-WAS-LIVE", {
          status: "online",
          lastSeenAt: "2026-07-15T09:00:00Z",
        }),
      ],
      livePatches: [],
      dataSource: "api",
      connectivityNowMs: now,
      afterLiveRefresh: true,
    });

    expect(display).toHaveLength(1);
    expect(display[0]?.vehicleId).toBe("VH-WAS-LIVE");
    expect(display[0]?.status).toBe("offline");
  });

  it("tras Actualizar admite vehículos nuevos en línea por SSE", () => {
    const display = resolveDisplayVehicles({
      vehicles: [vehicle("VH-ON")],
      livePatches: [vehicle("VH-NEW")],
      dataSource: "api",
      connectivityNowMs: now,
      afterLiveRefresh: true,
    });

    expect(display.map((v) => v.vehicleId).sort()).toEqual(["VH-NEW", "VH-ON"]);
  });
});
