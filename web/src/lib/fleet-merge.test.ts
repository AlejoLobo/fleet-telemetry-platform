import { describe, expect, it } from "vitest";
import {
  compareVehicleRecency,
  mergeVehicleUpdates,
  pruneVehiclePatches,
} from "@/lib/fleet-merge";
import type { VehicleStatus } from "@/types/fleet";
import { TEST_DEVICE_1, TEST_DEVICE_2, TEST_DEVICE_3, testDeviceId, testVehicle } from "@/test/device-fixtures";

function vehicle(
  id: string,
  overrides: Partial<VehicleStatus> = {},
): VehicleStatus {
  return testVehicle(id, overrides);
}

describe("mergeVehicleUpdates", () => {
  it("Vehicle_update_individual_actualiza_solo_un_vehiculo", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001"), vehicle("00000000-0000-4000-8000-000000000002", { status: "offline" })];
    const updates = [vehicle("00000000-0000-4000-8000-000000000001", { status: "offline" })];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.status).toBe("offline");
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000002")?.status).toBe("offline");
  });

  it("Actualizacion_no_elimina_los_demas_vehiculos", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001"), vehicle("00000000-0000-4000-8000-000000000002"), vehicle("00000000-0000-4000-8000-000000000003")];
    const updates = [vehicle("00000000-0000-4000-8000-000000000002", { status: "offline" })];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged).toHaveLength(3);
    expect(merged.map((v) => v.deviceId).sort()).toEqual(["00000000-0000-4000-8000-000000000001", "00000000-0000-4000-8000-000000000002", "00000000-0000-4000-8000-000000000003"]);
  });

  it("Actualizaciones_repetidas_no_duplican_vehiculo", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001"), vehicle("00000000-0000-4000-8000-000000000002")];
    const updates = [
      vehicle("00000000-0000-4000-8000-000000000001", { status: "offline" }),
      vehicle("00000000-0000-4000-8000-000000000001", { status: "online" }),
    ];

    const merged = mergeVehicleUpdates(snapshot, updates);
    expect(merged.filter((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")).toHaveLength(1);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.status).toBe("online");
  });

  it("DashboardPage_no_reemplaza_toda_la_flota_con_un_solo_vehiculo", () => {
    const baseFleet = [vehicle("00000000-0000-4000-8000-000000000001"), vehicle("00000000-0000-4000-8000-000000000002"), vehicle("00000000-0000-4000-8000-000000000003")];
    const ssePatches: VehicleStatus[] = [];
    const incoming = [vehicle("00000000-0000-4000-8000-000000000002", { status: "offline" })];

    const nextPatches = mergeVehicleUpdates(ssePatches, incoming);
    const displayVehicles = mergeVehicleUpdates(baseFleet, nextPatches);

    expect(displayVehicles).toHaveLength(3);
    expect(displayVehicles.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000002")?.status).toBe("offline");
  });

  it("SSE_llega_antes_de_terminar_snapshot_y_no_se_pierde", () => {
    const staleSnapshot = [vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T09:00:00Z", lastSpeedKmh: 10 })];
    const ssePatch = [
      vehicle("00000000-0000-4000-8000-000000000001", {
        lastSeenAt: "2026-07-10T10:05:00Z",
        lastSpeedKmh: 88,
        status: "online",
      }),
    ];

    const patches = mergeVehicleUpdates([], ssePatch);
    const display = mergeVehicleUpdates(staleSnapshot, patches);

    expect(display.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSpeedKmh).toBe(88);
    expect(display.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSeenAt).toBe("2026-07-10T10:05:00Z");
  });

  it("Snapshot_antiguo_no_sobrescribe_parche_nuevo", () => {
    const staleSnapshot = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T09:00:00Z", status: "offline" }),
    ];
    const freshPatch = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T10:05:00Z", status: "online" }),
    ];

    const merged = mergeVehicleUpdates(staleSnapshot, freshPatch);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.status).toBe("online");
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSeenAt).toBe("2026-07-10T10:05:00Z");
  });

  it("Snapshot_nuevo_reemplaza_parche_antiguo", () => {
    const freshSnapshot = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T11:00:00Z", status: "online", lastSpeedKmh: 55 }),
    ];
    const stalePatch = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T10:00:00Z", status: "offline", lastSpeedKmh: 0 }),
    ];

    const merged = mergeVehicleUpdates(freshSnapshot, stalePatch);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSpeedKmh).toBe(55);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSeenAt).toBe("2026-07-10T11:00:00Z");
  });

  it("Timestamp_invalido_no_causa_regresion", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T10:00:00Z", lastSpeedKmh: 50, lastEventId: "11111111-1111-1111-1111-111111111111" })];
    const invalidPatch = [vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "invalid-date", lastSpeedKmh: 99, lastEventId: "22222222-2222-2222-2222-222222222222" })];

    const merged = mergeVehicleUpdates(snapshot, invalidPatch);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSpeedKmh).toBe(50);
    expect(compareVehicleRecency(invalidPatch[0], snapshot[0])).toBeLessThan(0);
  });

  it("Timestamp_igual_EventId_mayor_conserva_parche", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastEventId: "11111111-1111-1111-1111-111111111111",
    })];
    const patch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 88,
      lastEventId: "22222222-2222-2222-2222-222222222222",
    })];

    const merged = mergeVehicleUpdates(snapshot, patch);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSpeedKmh).toBe(88);
  });

  it("Timestamp_igual_EventId_menor_conserva_snapshot", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 50,
      lastEventId: "22222222-2222-2222-2222-222222222222",
    })];
    const patch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastEventId: "11111111-1111-1111-1111-111111111111",
    })];

    const merged = mergeVehicleUpdates(snapshot, patch);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSpeedKmh).toBe(50);
  });

  it("Carrera_snapshot_antiguo_y_SSE_nuevo_con_timestamp_igual", () => {
    const staleSnapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastEventId: "11111111-1111-1111-1111-111111111111",
    })];
    const freshPatch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 70,
      lastEventId: "33333333-3333-3333-3333-333333333333",
    })];

    const merged = mergeVehicleUpdates(staleSnapshot, freshPatch);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.lastSpeedKmh).toBe(70);
  });

  it("Offline_mismo_EventId_gana_a_snapshot_online_mas_antiguo", () => {
    const staleOnlineSnapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "online",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T10:00:00Z",
    })];
    const offlinePatch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "offline",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T10:05:00Z",
    })];

    const merged = mergeVehicleUpdates(staleOnlineSnapshot, offlinePatch);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.status).toBe("offline");
  });

  it("Igual_EventId_e_igual_timestamp_no_depende_del_orden_de_render", () => {
    const olderEval = vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "online",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T10:00:00Z",
    });
    const newerEval = vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "offline",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T10:05:00Z",
    });

    const forward = mergeVehicleUpdates([olderEval], [newerEval]);
    const reverse = mergeVehicleUpdates([newerEval], [olderEval]);

    expect(forward.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.status).toBe("offline");
    expect(reverse.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.status).toBe("offline");
  });

  it("Parche_velocidad_conserva_vehicleType_sin_payload", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      vehicleType: "truck",
      vehicleName: "VH-001",
    })];
    const speedPatch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:05:00Z",
      lastSpeedKmh: 99,
      vehicleType: "car",
      vehicleTypeFromPayload: false,
    })];

    const merged = mergeVehicleUpdates(snapshot, speedPatch);
    expect(merged[0]?.vehicleType).toBe("truck");
    expect(merged[0]?.lastSpeedKmh).toBe(99);
  });

  it("Parche_con_vehicleTypeFromPayload_actualiza_tipo", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      vehicleType: "car",
    })];
    const typePatch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:05:00Z",
      vehicleType: "motorcycle",
      vehicleTypeFromPayload: true,
    })];

    const merged = mergeVehicleUpdates(snapshot, typePatch);
    expect(merged[0]?.vehicleType).toBe("motorcycle");
  });

  it("Parche_sin_nombre_conserva_vehicleName_base", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      vehicleName: "VH-001",
    })];
    const patch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:05:00Z",
      vehicleName: "  ",
    })];

    const merged = mergeVehicleUpdates(snapshot, patch);
    expect(merged[0]?.vehicleName).toBe("VH-001");
  });

  it("deviceId_permanece_estable_al_fusionar", () => {
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      deviceId: "00000000-0000-4000-8000-000000000001",
    })];
    const patch = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:05:00Z",
      deviceId: "00000000-0000-4000-8000-000000000099",
    })];

    const merged = mergeVehicleUpdates(snapshot, patch);
    expect(merged[0]?.deviceId).toBe("00000000-0000-4000-8000-000000000001");
  });

  it("Carrera_snapshot_iniciado_antes_de_expiracion_no_regresa_online", () => {
    const staleOnlineSnapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "online",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T09:55:00Z",
    })];
    const offlineAfterExpiry = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "offline",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T10:06:00Z",
    })];

    const merged = mergeVehicleUpdates(staleOnlineSnapshot, offlineAfterExpiry);
    expect(merged.find((v) => v.deviceId === "00000000-0000-4000-8000-000000000001")?.status).toBe("offline");
  });
});

describe("pruneVehiclePatches", () => {
  it("Refresh_no_elimina_actualizacion_mas_reciente", () => {
    const patches = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T10:05:00Z", lastSpeedKmh: 88 }),
    ];
    const staleSnapshot = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T09:00:00Z", lastSpeedKmh: 10 }),
    ];

    const pruned = pruneVehiclePatches(patches, staleSnapshot);
    expect(pruned).toHaveLength(1);
    expect(pruned[0]?.lastSpeedKmh).toBe(88);
  });

  it("Snapshot_nuevo_reemplaza_parche_antiguo_via_prune", () => {
    const patches = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T09:30:00Z", lastSpeedKmh: 20 }),
    ];
    const freshSnapshot = [
      vehicle("00000000-0000-4000-8000-000000000001", { lastSeenAt: "2026-07-10T10:00:00Z", lastSpeedKmh: 40 }),
    ];

    const pruned = pruneVehiclePatches(patches, freshSnapshot);
    expect(pruned).toHaveLength(0);
  });

  it("Prune_conserva_offline_evaluado_despues", () => {
    const patches = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "offline",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T10:06:00Z",
    })];
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "online",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T09:55:00Z",
    })];

    const pruned = pruneVehiclePatches(patches, snapshot);
    expect(pruned).toHaveLength(1);
    expect(pruned[0]?.status).toBe("offline");
  });

  it("Snapshot_offline_nuevo_elimina_parche_obsoleto", () => {
    const patches = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "online",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T09:55:00Z",
    })];
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      status: "offline",
      lastEventId: "11111111-1111-1111-1111-111111111111",
      statusEvaluatedAt: "2026-07-10T10:06:00Z",
    })];

    const pruned = pruneVehiclePatches(patches, snapshot);
    expect(pruned).toHaveLength(0);
  });

  it("Prune_no_elimina_parche_con_EventId_mayor", () => {
    const patches = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 88,
      lastEventId: "33333333-3333-3333-3333-333333333333",
    })];
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastEventId: "11111111-1111-1111-1111-111111111111",
    })];

    const pruned = pruneVehiclePatches(patches, snapshot);
    expect(pruned).toHaveLength(1);
    expect(pruned[0]?.lastSpeedKmh).toBe(88);
  });

  it("Snapshot_con_EventId_mayor_elimina_parche", () => {
    const patches = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 20,
      lastEventId: "11111111-1111-1111-1111-111111111111",
    })];
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001", {
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 60,
      lastEventId: "22222222-2222-2222-2222-222222222222",
    })];

    const pruned = pruneVehiclePatches(patches, snapshot);
    expect(pruned).toHaveLength(0);
  });

  it("conserva_parches_de_vehiculos_ausentes_en_snapshot", () => {
    const patches = [vehicle("00000000-0000-4000-8000-000000000099", { lastSeenAt: "2026-07-10T10:05:00Z" })];
    const snapshot = [vehicle("00000000-0000-4000-8000-000000000001")];

    const pruned = pruneVehiclePatches(patches, snapshot);
    expect(pruned).toHaveLength(1);
    expect(pruned[0]?.deviceId).toBe("00000000-0000-4000-8000-000000000099");
  });
});
