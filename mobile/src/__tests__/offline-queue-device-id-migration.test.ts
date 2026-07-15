import { createSqliteMemoryDb, getSqliteRows, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

import {
  enqueueEvent,
  getQueueEventByEventId,
  migratePendingEventsToDeviceId,
  resetOfflineQueueForTests,
  toPayload,
} from "@/db/offline-queue";
import { getSqliteRows as rows } from "@/__tests__/helpers/sqlite-memory";

const STABLE_DEVICE_ID = "aaaaaaaa-bbbb-4ccc-8ddd-000000000099";

describe("migratePendingEventsToDeviceId", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
  });

  async function seedLegacy(eventId: string, vehicleId: string, status: "pending" | "synced" = "pending") {
    // Inserta con device_id = vehicle_id (legado inválido).
    await enqueueEvent({
      eventId,
      deviceId: vehicleId,
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
      row.vehicle_id = vehicleId;
      row.device_id = vehicleId;
      if (status === "synced") {
        row.status = "synced";
        row.synced_at = new Date().toISOString();
      }
    }
  }

  it("migra VH-001 y Camión Pereira al mismo UUID estable", async () => {
    await seedLegacy("e1", "VH-001");
    await seedLegacy("e2", "Camión Pereira");

    const updated = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(updated).toBe(2);

    const a = await getQueueEventByEventId("e1");
    const b = await getQueueEventByEventId("e2");
    expect(a?.deviceId).toBe(STABLE_DEVICE_ID);
    expect(b?.deviceId).toBe(STABLE_DEVICE_ID);
    expect(toPayload(a!).deviceId).toBe(STABLE_DEVICE_ID);
    expect(toPayload(b!).deviceId).toBe(STABLE_DEVICE_ID);
  });

  it("no modifica eventos synced", async () => {
    await seedLegacy("e-synced", "VH-001", "synced");
    await seedLegacy("e-pending", "VH-001", "pending");

    await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);

    const synced = rows().find((r) => r.event_id === "e-synced");
    const pending = await getQueueEventByEventId("e-pending");
    expect(synced?.device_id).toBe("VH-001");
    expect(pending?.deviceId).toBe(STABLE_DEVICE_ID);
  });

  it("idempotente si ya tiene el UUID estable", async () => {
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
    const first = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    const second = await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    expect(first).toBe(0);
    expect(second).toBe(0);
  });

  it("batch resultante usa un solo DeviceId", async () => {
    await seedLegacy("b1", "VH-001");
    await seedLegacy("b2", "Camión Pereira");
    await migratePendingEventsToDeviceId(STABLE_DEVICE_ID);
    const ids = rows()
      .filter((r) => r.status === "pending")
      .map((r) => r.device_id);
    expect(new Set(ids).size).toBe(1);
    expect(ids[0]).toBe(STABLE_DEVICE_ID);
  });
});
