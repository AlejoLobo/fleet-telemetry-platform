import { describe, expect, it } from "vitest";
import { parseFleetUpdatePayload, parseVehicleUpdatePayload } from "@/lib/sse-parser";
import { REALTIME_EVENTS } from "@/lib/realtime-events";

describe("sse parser canonical contract", () => {
  it("parsea_vehicle_update_individual", () => {
    const payload = JSON.stringify({
      vehicleId: "VH-001",
      name: "VH-001",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 45,
      lastLatitude: 4.6,
      lastLongitude: -74.0,
    });

    const vehicle = parseVehicleUpdatePayload(payload);
    expect(vehicle?.vehicleId).toBe("VH-001");
    expect(vehicle?.lastSpeedKmh).toBe(45);
  });

  it("parsea_fleet_update_array_legacy", () => {
    const payload = JSON.stringify([
      { vehicleId: "VH-001", status: "online", lastSeenAt: "2026-07-10T10:00:00Z" },
      { vehicleId: "VH-002", status: "offline", lastSeenAt: "2026-07-10T09:00:00Z" },
    ]);

    const vehicles = parseFleetUpdatePayload(payload);
    expect(vehicles).toHaveLength(2);
  });

  it("contrato_canonico_usa_vehicle_update", () => {
    expect(REALTIME_EVENTS.vehicleUpdate).toBe("vehicle-update");
    expect(REALTIME_EVENTS.fleetUpdate).toBe("fleet-update");
  });
});
