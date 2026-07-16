import { createSqliteMemoryDb, getSqliteRows, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

import {
  claimNextBatch,
  enqueueEvent,
  getQueueEventByEventId,
  migratePendingEventsToDeviceId,
  resetOfflineQueueForTests,
  toPayload,
} from "@/db/offline-queue";
import { getSqliteRows as rows } from "@/__tests__/helpers/sqlite-memory";

const STABLE_DEVICE_ID = "aaaaaaaa-bbbb-4ccc-8ddd-000000000099";
const OTHER_UUID = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

describe("migratePendingEventsToDeviceId", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
  });

  async function seedLegacy(
    eventId: string,
    vehicleId: string | null,
    status: "pending" | "synced" | "permanent_failure" = "pending",
  ) {
    await enqueueEvent({
      eventId,
      deviceId: vehicleId && vehicleId.length > 0 ? vehicleId : STABLE_DEVICE_ID,
      driverId: "DRV-001",
      timestamp: "2026-07-10T10:00:00Z",
      latitude: 4.65,
      longitude: -74.08,
      speedKmh: 40,
      fuelLevelPercent: 70,
      batteryPercent: 90,
    });
    const row = getSqliteRows().find((r) => r.event_id === eventId);
    if (row) {
      row.vehicle_id = vehicleId ?? "";
      row.device_id = vehicleId ?? "";
      if (status === "synced") {
        row.status = "synced";
        row.synced_at = new Date().toISOString();
      }
      if (status === "permanent_failure") {
        row.status = "permanent_failure";
        row.last_error = "invalid";
      }
    }
  }

  it("migra VH-001 al UUID estable", async () => {
    await seedLegacy("e-vh", "VH-001");
    const result = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(result.migrated).toBe(1);
    expect(result.conflicts).toHaveLength(0);
    expect((await getQueueEventByEventId("e-vh"))?.deviceId).toBe(STABLE_DEVICE_ID);
  });

  it("migra nombre libre al UUID estable", async () => {
    await seedLegacy("e-free", "Camión Pereira");
    const result = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(result.migrated).toBe(1);
    expect((await getQueueEventByEventId("e-free"))?.deviceId).toBe(STABLE_DEVICE_ID);
  });

  it("migra device_id null/vacío", async () => {
    await seedLegacy("e-null", null);
    const result = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(result.migrated).toBe(1);
    expect((await getQueueEventByEventId("e-null"))?.deviceId).toBe(STABLE_DEVICE_ID);
  });

  it("UUID igual no cambia", async () => {
    await enqueueEvent({
      eventId: "e-ok",
      deviceId: STABLE_DEVICE_ID,
      driverId: null,
      timestamp: "2026-07-10T10:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    });
    const result = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(result.migrated).toBe(0);
    expect(result.unchanged).toBe(1);
    expect(result.conflicts).toHaveLength(0);
  });

  it("UUID distinto no se sobrescribe y se reporta conflicto", async () => {
    await enqueueEvent({
      eventId: "e-conflict",
      deviceId: OTHER_UUID,
      driverId: null,
      timestamp: "2026-07-10T10:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    });
    const result = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(result.migrated).toBe(0);
    expect(result.conflicts).toEqual([
      {
        eventId: "e-conflict",
        storedDeviceId: OTHER_UUID,
        currentDeviceId: STABLE_DEVICE_ID,
      },
    ]);
    expect((await getQueueEventByEventId("e-conflict"))?.deviceId).toBe(OTHER_UUID);
  });

  it("batch no mezcla identidades: claim solo del DeviceId actual", async () => {
    await seedLegacy("b1", "VH-001");
    await enqueueEvent({
      eventId: "b2",
      deviceId: OTHER_UUID,
      driverId: null,
      timestamp: "2026-07-10T10:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    });
    await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    const claimed = await claimNextBatch(10, new Date().toISOString(), STABLE_DEVICE_ID);
    expect(claimed.map((c) => c.eventId)).toEqual(["b1"]);
    expect(claimed.every((c) => c.deviceId === STABLE_DEVICE_ID)).toBe(true);
    expect(rows().find((r) => r.event_id === "b2")?.status).toBe("pending");
    expect(rows().find((r) => r.event_id === "b2")?.device_id).toBe(OTHER_UUID);
  });

  it("no modifica eventos synced", async () => {
    await seedLegacy("e-synced", "VH-001", "synced");
    await seedLegacy("e-pending", "VH-001", "pending");
    await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(rows().find((r) => r.event_id === "e-synced")?.device_id).toBe("VH-001");
    expect((await getQueueEventByEventId("e-pending"))?.deviceId).toBe(STABLE_DEVICE_ID);
  });

  it("no modifica permanent_failure", async () => {
    await seedLegacy("e-perm", "VH-001", "permanent_failure");
    const result = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(result.migrated).toBe(0);
    expect(rows().find((r) => r.event_id === "e-perm")?.device_id).toBe("VH-001");
  });

  it("idempotente si ya tiene el UUID estable", async () => {
    await enqueueEvent({
      eventId: "e-ok2",
      deviceId: STABLE_DEVICE_ID,
      driverId: null,
      timestamp: "2026-07-10T10:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    });
    const first = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    const second = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(first.migrated).toBe(0);
    expect(first.unchanged).toBe(1);
    expect(second.migrated).toBe(0);
    expect(second.unchanged).toBe(1);
  });

  it("payload migrado usa toPayload con DeviceId estable", async () => {
    await seedLegacy("e-payload", "VH-001");
    await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    const event = await getQueueEventByEventId("e-payload");
    expect(toPayload(event!).deviceId).toBe(STABLE_DEVICE_ID);
  });
});
