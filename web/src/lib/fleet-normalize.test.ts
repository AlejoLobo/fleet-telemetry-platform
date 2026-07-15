import { describe, expect, it } from "vitest";
import { normalizeVehicle } from "@/lib/fleet-normalize";

describe("fleet-normalize", () => {
  it("conserva el nombre VH-001 aunque el vehicleId sea UUID", () => {
    const vehicle = normalizeVehicle({
      vehicleId: "device-uuid-not-really",
      name: "VH-001",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastLatitude: 1,
      lastLongitude: 1,
    });

    // device-uuid-not-really no es UUID formal; name VH-001 se conserva
    expect(vehicle.name).toBe("VH-001");
  });

  it("no promove un UUID a name", () => {
    const vehicle = normalizeVehicle({
      vehicleId: "11111111-1111-1111-1111-111111111111",
      name: "11111111-1111-1111-1111-111111111111",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastLatitude: 1,
      lastLongitude: 1,
    });

    expect(vehicle.name).toBe("");
  });

  it("conserva el nombre operativo distinto del id", () => {
    const vehicle = normalizeVehicle({
      vehicleId: "11111111-1111-1111-1111-111111111111",
      Name: "VH-001",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastLatitude: 1,
      lastLongitude: 1,
    });

    expect(vehicle.name).toBe("VH-001");
  });
});
