/**
 * Serialización de apertura + migración SQLite (readyDbPromise).
 */
import { createSqliteMemoryDb, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();
let mockOpenCalls = 0;
let mockMigrateTransactions = 0;
let mockFailNextMigrate = false;

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => {
    mockOpenCalls += 1;
    return {
      ...mockMemoryDb,
      withTransactionAsync: jest.fn(async (cb: () => Promise<void>) => {
        mockMigrateTransactions += 1;
        if (mockFailNextMigrate) {
          mockFailNextMigrate = false;
          throw new Error("migración simulada fallida");
        }
        return cb();
      }),
      getAllAsync: jest.fn(async (sql: string, ...args: unknown[]) => {
        if (sql.includes("PRAGMA table_info")) {
          return [
            { name: "local_id" },
            { name: "event_id" },
            { name: "vehicle_id" },
            { name: "device_id" },
            { name: "driver_id" },
            { name: "timestamp" },
            { name: "latitude" },
            { name: "longitude" },
            { name: "speed_kmh" },
            { name: "fuel_level_percent" },
            { name: "battery_percent" },
            { name: "source" },
            { name: "status" },
            { name: "retry_count" },
            { name: "next_attempt_at" },
            { name: "last_attempt_at" },
            { name: "last_error" },
            { name: "locked_at" },
            { name: "synced_at" },
            { name: "created_at" },
          ];
        }
        return mockMemoryDb.getAllAsync(sql, ...args);
      }),
    };
  }),
}));

import {
  countPendingEvents,
  enqueueEvent,
  getOfflineQueueInitStatsForTests,
  resetOfflineQueueForTests,
} from "@/db/offline-queue";

const DEVICE_ID = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

function sampleEvent(eventId: string) {
  return {
    eventId,
    deviceId: DEVICE_ID,
    driverId: "DRV",
    timestamp: "2026-07-15T12:00:00Z",
    latitude: 1,
    longitude: 2,
    speedKmh: 3,
    fuelLevelPercent: null,
    batteryPercent: null,
  };
}

describe("inicialización SQLite única y serializada", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    mockOpenCalls = 0;
    mockMigrateTransactions = 0;
    mockFailNextMigrate = false;
  });

  it("diez llamadas concurrentes abren y migran una sola vez", async () => {
    await Promise.all(Array.from({ length: 10 }, () => countPendingEvents()));
    expect(mockOpenCalls).toBe(1);
    expect(mockMigrateTransactions).toBe(1);
    expect(getOfflineQueueInitStatsForTests()).toEqual({ openCount: 1, migrateCount: 1 });
  });

  it("enqueue y count simultáneos comparten la misma migración", async () => {
    await Promise.all([
      enqueueEvent(sampleEvent("11111111-1111-1111-1111-111111111111"), "simulated"),
      countPendingEvents(),
      enqueueEvent(sampleEvent("22222222-2222-2222-2222-222222222222"), "simulated"),
      countPendingEvents(),
    ]);
    expect(mockOpenCalls).toBe(1);
    expect(mockMigrateTransactions).toBe(1);
    expect(getOfflineQueueInitStatsForTests().migrateCount).toBe(1);
  });

  it("migración exitosa no se repite en operaciones posteriores", async () => {
    await countPendingEvents();
    await enqueueEvent(sampleEvent("33333333-3333-3333-3333-333333333333"), "simulated");
    await countPendingEvents();
    expect(mockOpenCalls).toBe(1);
    expect(mockMigrateTransactions).toBe(1);
  });

  it("migración fallida limpia el caché y permite reintento", async () => {
    mockFailNextMigrate = true;
    await expect(countPendingEvents()).rejects.toThrow(/migración simulada fallida/);
    expect(getOfflineQueueInitStatsForTests().openCount).toBe(1);

    await expect(countPendingEvents()).resolves.toBeGreaterThanOrEqual(0);
    expect(mockOpenCalls).toBe(2);
    expect(mockMigrateTransactions).toBe(2);
    expect(getOfflineQueueInitStatsForTests()).toEqual({ openCount: 2, migrateCount: 2 });
  });

  it("cinco enqueues consecutivos migran una sola vez", async () => {
    for (let i = 0; i < 5; i += 1) {
      await enqueueEvent(
        sampleEvent(`44444444-4444-4444-4444-44444444444${i}`),
        "simulated",
      );
    }
    expect(getOfflineQueueInitStatsForTests().migrateCount).toBe(1);
    expect(mockOpenCalls).toBe(1);
  });
});
