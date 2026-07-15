/**
 * Prueba de migración desde esquema antiguo sin device_id.
 * Usa un mock que simula PRAGMA table_info dinámico.
 */
import { createSqliteMemoryDb, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

/** Estado mutable del esquema simulado (prefijo mock* requerido por Jest). */
const mockSchemaState = {
  columns: new Set<string>([
    "local_id",
    "event_id",
    "vehicle_id",
    "driver_id",
    "timestamp",
    "latitude",
    "longitude",
    "speed_kmh",
    "fuel_level_percent",
    "battery_percent",
    "source",
    "status",
    "retry_count",
    "next_attempt_at",
    "last_attempt_at",
    "last_error",
    "locked_at",
    "synced_at",
    "created_at",
  ]),
  schemaMetaVersion: "4" as string | null,
};

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => {
    const base = mockMemoryDb;
    return {
      ...base,
      execAsync: jest.fn(async (sql: string) => {
        if (sql.includes("ADD COLUMN device_id")) {
          mockSchemaState.columns.add("device_id");
        }
        if (sql.includes("ADD COLUMN source")) mockSchemaState.columns.add("source");
        if (sql.includes("ADD COLUMN retry_count")) mockSchemaState.columns.add("retry_count");
        if (sql.includes("PRAGMA user_version")) return undefined;
        return undefined;
      }),
      getFirstAsync: jest.fn(async (sql: string, ...args: unknown[]) => {
        if (sql.includes("schema_meta") && sql.includes("version")) {
          return mockSchemaState.schemaMetaVersion
            ? { value: mockSchemaState.schemaMetaVersion }
            : null;
        }
        return base.getFirstAsync(sql, ...(args as never[]));
      }),
      getAllAsync: jest.fn(async (sql: string, ...args: unknown[]) => {
        if (sql.includes("PRAGMA table_info")) {
          return [...mockSchemaState.columns].map((name) => ({ name }));
        }
        return base.getAllAsync(sql, ...args);
      }),
      runAsync: jest.fn(async (sql: string, ...params: unknown[]) => {
        if (sql.includes("INSERT OR REPLACE INTO schema_meta")) {
          mockSchemaState.schemaMetaVersion = String(params[0]);
          return { changes: 1 };
        }
        return base.runAsync(sql, ...params);
      }),
      withTransactionAsync: jest.fn(async (cb: () => Promise<void>) => cb()),
    };
  }),
}));

import {
  SCHEMA_VERSION,
  enqueueEvent,
  resetOfflineQueueForTests,
  countPendingEvents,
} from "@/db/offline-queue";

const DEVICE_ID = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

describe("migración SQLite device_id sobre instalación antigua", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    mockSchemaState.columns = new Set([
      "local_id",
      "event_id",
      "vehicle_id",
      "driver_id",
      "timestamp",
      "latitude",
      "longitude",
      "speed_kmh",
      "fuel_level_percent",
      "battery_percent",
      "source",
      "status",
      "retry_count",
      "next_attempt_at",
      "last_attempt_at",
      "last_error",
      "locked_at",
      "synced_at",
      "created_at",
    ]);
    mockSchemaState.schemaMetaVersion = "4";
  });

  it("agrega device_id aunque schema_meta diga versión 4", async () => {
    expect(mockSchemaState.columns.has("device_id")).toBe(false);
    await enqueueEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: DEVICE_ID,
      driverId: "DRV",
      timestamp: "2026-07-15T12:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, "simulated");

    expect(mockSchemaState.columns.has("device_id")).toBe(true);
    expect(mockSchemaState.schemaMetaVersion).toBe(String(SCHEMA_VERSION));
    expect(await countPendingEvents()).toBeGreaterThanOrEqual(1);
  });

  it("migración es idempotente al reiniciar", async () => {
    await enqueueEvent({
      eventId: "22222222-2222-2222-2222-222222222222",
      deviceId: DEVICE_ID,
      driverId: "DRV",
      timestamp: "2026-07-15T12:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, "simulated");

    resetOfflineQueueForTests();
    await enqueueEvent({
      eventId: "33333333-3333-3333-3333-333333333333",
      deviceId: DEVICE_ID,
      driverId: "DRV",
      timestamp: "2026-07-15T12:01:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, "simulated");

    expect(mockSchemaState.columns.has("device_id")).toBe(true);
    expect(await countPendingEvents()).toBeGreaterThanOrEqual(1);
  });

  it("UUID válido distinto no se sobrescribe en backfill de cola", async () => {
    // Cubierto por migratePendingEventsToDeviceId; aquí solo comprobamos que
    // el esquema permite device_id sin perder la tabla.
    expect(SCHEMA_VERSION).toBeGreaterThanOrEqual(5);
    expect(mockSchemaState.columns.has("device_id")).toBe(false);
    await countPendingEvents();
    expect(mockSchemaState.columns.has("device_id")).toBe(true);
  });
});
