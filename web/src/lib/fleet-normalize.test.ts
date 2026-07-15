import { describe, expect, it } from "vitest";
import { normalizeVehicle } from "@/lib/fleet-normalize";

describe("fleet-normalize", () => {
  it("no promove el VehicleId a name", () => {
    const vehicle = normalizeVehicle({
      vehicleId: "device-uuid",
      name: "device-uuid",
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
      vehicleId: "device-uuid",
      Name: "Camión norte",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastLatitude: 1,
      lastLongitude: 1,
    });

    expect(vehicle.name).toBe("Camión norte");
  });
});
