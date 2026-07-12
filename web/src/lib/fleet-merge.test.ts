import { describe, expect, it } from "vitest";
import {
  compareVehicleRecency,
  mergeVehicleUpdates,
  pruneVehiclePatches,
} from "@/lib/fleet-merge";
import type { VehicleStatus } from "@/types/fleet";

function vehicle(
  id: string,
  overrides: Partial<VehicleStatus> = {},
): VehicleStatus {
  return {
    vehicleId: id,
    name: id,
    status: "online",
    lastSeenAt: "2026-07-10T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
    ...overrides,
  };
}

describe("mergeVehicleUpdates", () => {
  it("Vehicle_update_individual_actualiza_solo_un_vehiculo", () => {
    const snapshot = [vehicle("VH-001"), vehicle("VH-002", { status: "offline" })];
    const updates = [vehicle("VH-001", { status: "offline" })];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.status).toBe("offline");
    expect(merged.find((v) => v.vehicleId === "VH-002")?.status).toBe("offline");
  });

  it("Actualizacion_no_elimina_los_demas_vehiculos", () => {
    const snapshot = [vehicle("VH-001"), vehicle("VH-002"), vehicle("VH-003")];
    const updates = [vehicle("VH-002", { status: "offline" })];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged).toHaveLength(3);
    expect(merged.map((v) => v.vehicleId).sort()).toEqual(["VH-001", "VH-002", "VH-003"]);
  });

  it("Actualizaciones_repetidas_no_duplican_vehiculo", () => {
    const snapshot = [vehicle("VH-001"), vehicle("VH-002")];
    const updates = [
      vehicle("VH-001", { status: "offline" }),
      vehicle("VH-001", { status: "online" }),
    ];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged.filter((v) => v.vehicleId === "VH-001")).toHaveLength(1);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.status).toBe("online");
  });

  it("DashboardPage_no_reemplaza_toda_la_flota_con_un_solo_vehiculo", () => {
    const baseFleet = [vehicle("VH-001"), vehicle("VH-002"), vehicle("VH-003")];
    const ssePatches: VehicleStatus[] = [];
    const incoming = [vehicle("VH-002", { status: "offline" })];

    const nextPatches = mergeVehicleUpdates(ssePatches, incoming);
    const displayVehicles = mergeVehicleUpdates(baseFleet, nextPatches);

    expect(displayVehicles).toHaveLength(3);
    expect(displayVehicles.find((v) => v.vehicleId === "VH-002")?.status).toBe("offline");
  });

  it("SSE_llega_antes_de_terminar_snapshot_y_no_se_pierde", () => {
    const staleSnapshot = [vehicle("VH-001", { lastSeenAt: "2026-07-10T09:00:00Z", lastSpeedKmh: 10 })];
    const ssePatch = [
      vehicle("VH-001", {
        lastSeenAt: "2026-07-10T10:05:00Z",
        lastSpeedKmh: 88,
        status: "online",
      }),
    ];

    const patches = mergeVehicleUpdates([], ssePatch);
    const display = mergeVehicleUpdates(staleSnapshot, patches);

    expect(display.find((v) => v.vehicleId === "VH-001")?.lastSpeedKmh).toBe(88);
    expect(display.find((v) => v.vehicleId === "VH-001")?.lastSeenAt).toBe("2026-07-10T10:05:00Z");
  });

  it("Snapshot_antiguo_no_sobrescribe_parche_nuevo", () => {
    const staleSnapshot = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T09:00:00Z", status: "offline" }),
    ];
    const freshPatch = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T10:05:00Z", status: "online" }),
    ];

    const merged = mergeVehicleUpdates(staleSnapshot, freshPatch);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.status).toBe("online");
    expect(merged.find((v) => v.vehicleId === "VH-001")?.lastSeenAt).toBe("2026-07-10T10:05:00Z");
  });

  it("Snapshot_nuevo_reemplaza_parche_antiguo", () => {
    const freshSnapshot = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T11:00:00Z", status: "online", lastSpeedKmh: 55 }),
    ];
    const stalePatch = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T10:00:00Z", status: "offline", lastSpeedKmh: 0 }),
    ];

    const merged = mergeVehicleUpdates(freshSnapshot, stalePatch);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.lastSpeedKmh).toBe(55);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.lastSeenAt).toBe("2026-07-10T11:00:00Z");
  });

  it("Timestamp_invalido_no_causa_regresion", () => {
    const snapshot = [vehicle("VH-001", { lastSeenAt: "2026-07-10T10:00:00Z", lastSpeedKmh: 50 })];
    const invalidPatch = [vehicle("VH-001", { lastSeenAt: "invalid-date", lastSpeedKmh: 99 })];

    const merged = mergeVehicleUpdates(snapshot, invalidPatch);
    expect(merged.find((v) => v.vehicleId === "VH-001")?.lastSpeedKmh).toBe(50);
    expect(compareVehicleRecency(invalidPatch[0], snapshot[0])).toBeLessThan(0);
  });
});

describe("pruneVehiclePatches", () => {
  it("Refresh_no_elimina_actualizacion_mas_reciente", () => {
    const patches = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T10:05:00Z", lastSpeedKmh: 88 }),
    ];
    const staleSnapshot = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T09:00:00Z", lastSpeedKmh: 10 }),
    ];

    const pruned = pruneVehiclePatches(patches, staleSnapshot);
    expect(pruned).toHaveLength(1);
    expect(pruned[0]?.lastSpeedKmh).toBe(88);
  });

  it("Snapshot_nuevo_reemplaza_parche_antiguo_via_prune", () => {
    const patches = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T09:30:00Z", lastSpeedKmh: 20 }),
    ];
    const freshSnapshot = [
      vehicle("VH-001", { lastSeenAt: "2026-07-10T10:00:00Z", lastSpeedKmh: 40 }),
    ];

    const pruned = pruneVehiclePatches(patches, freshSnapshot);
    expect(pruned).toHaveLength(0);
  });

  it("conserva_parches_de_vehiculos_ausentes_en_snapshot", () => {
    const patches = [vehicle("VH-NEW", { lastSeenAt: "2026-07-10T10:05:00Z" })];
    const snapshot = [vehicle("VH-001")];

    const pruned = pruneVehiclePatches(patches, snapshot);
    expect(pruned).toHaveLength(1);
    expect(pruned[0]?.vehicleId).toBe("VH-NEW");
  });
});
