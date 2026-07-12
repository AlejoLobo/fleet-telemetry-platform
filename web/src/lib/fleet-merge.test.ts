import { describe, expect, it } from "vitest";
import { mergeVehicleUpdates } from "@/lib/fleet-merge";
import type { VehicleStatus } from "@/types/fleet";

function vehicle(id: string, status: VehicleStatus["status"] = "online"): VehicleStatus {
  return {
    vehicleId: id,
    name: id,
    status,
    lastSeenAt: "2026-07-10T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
  };
}

describe("mergeVehicleUpdates", () => {
  it("Vehicle_update_individual_actualiza_solo_un_vehiculo", () => {
    const snapshot = [vehicle("VH-001"), vehicle("VH-002", "offline")];
    const updates = [vehicle("VH-001", "offline")];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.status).toBe("offline");
    expect(merged.find((v) => v.vehicleId === "VH-002")?.status).toBe("offline");
  });

  it("Actualizacion_no_elimina_los_demas_vehiculos", () => {
    const snapshot = [vehicle("VH-001"), vehicle("VH-002"), vehicle("VH-003")];
    const updates = [vehicle("VH-002", "offline")];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged).toHaveLength(3);
    expect(merged.map((v) => v.vehicleId).sort()).toEqual(["VH-001", "VH-002", "VH-003"]);
  });

  it("Actualizacion_repetida_no_duplica_vehicleId", () => {
    const snapshot = [vehicle("VH-001"), vehicle("VH-002")];
    const updates = [vehicle("VH-001", "offline"), vehicle("VH-001", "online")];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged.filter((v) => v.vehicleId === "VH-001")).toHaveLength(1);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.status).toBe("online");
  });

  it("DashboardPage_no_reemplaza_toda_la_flota_con_un_solo_vehiculo", () => {
    const baseFleet = [vehicle("VH-001"), vehicle("VH-002"), vehicle("VH-003")];
    const ssePatches: VehicleStatus[] = [];
    const incoming = [vehicle("VH-002", "offline")];

    const nextPatches = mergeVehicleUpdates(ssePatches, incoming);
    const displayVehicles = mergeVehicleUpdates(baseFleet, nextPatches);

    expect(displayVehicles).toHaveLength(3);
    expect(displayVehicles.find((v) => v.vehicleId === "VH-002")?.status).toBe("offline");
  });
});
