/**
 * Migración desde tabla telemetría legacy sin device_id (esquema pre-v3/v4).
 * Usa simulación fiel de PRAGMA table_info; no es prueba nativa de Expo SQLite.
 * Procedimiento Android físico: docs/mobile-sqlite-migration.md
 */
import { createSqliteMemoryDb, resetSqliteMemory, getSqliteRows, setSqliteMemoryNextLocalId } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

const mockSchemaState = {
  columns: new Set<string>(),
  schemaMetaVersion: null as string | null,
  userVersion: 0,
  alterDeviceIdCount: 0,
  indexCreates: [] as string[],
};

const LEGACY_COLUMNS = [
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
];

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => {
    const base = mockMemoryDb;
    return {
      ...base,
      execAsync: jest.fn(async (sql: string) => {
        if (sql.includes("ADD COLUMN device_id")) {
          mockSchemaState.alterDeviceIdCount += 1;
          mockSchemaState.columns.add("device_id");
        }
        if (sql.includes("ADD COLUMN source")) mockSchemaState.columns.add("source");
        if (sql.includes("ADD COLUMN retry_count")) mockSchemaState.columns.add("retry_count");
        if (sql.includes("CREATE INDEX")) {
          mockSchemaState.indexCreates.push(sql);
        }
        const userMatch = sql.match(/PRAGMA user_version\s*=\s*(\d+)/i);
        if (userMatch) {
          mockSchemaState.userVersion = Number(userMatch[1]);
        }
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
        if (sql.includes("INSERT INTO telemetry_queue") && !mockSchemaState.columns.has("device_id")) {
          throw new Error("table telemetry_queue has no column named device_id");
        }
        return base.runAsync(sql, ...params);
      }),
      withTransactionAsync: jest.fn(async (cb: () => Promise<void>) => cb()),
    };
  }),
}));

import {
  SCHEMA_VERSION,
  countPendingEvents,
  enqueueEvent,
  migratePendingEventsToDeviceId,
  resetOfflineQueueForTests,
  getOfflineQueueInitStatsForTests,
} from "@/db/offline-queue";

const LOCAL_UUID = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
const OTHER_UUID = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

describe("migración esquema legado sin device_id", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    mockSchemaState.columns = new Set(LEGACY_COLUMNS);
    mockSchemaState.schemaMetaVersion = "2";
    mockSchemaState.userVersion = 2;
    mockSchemaState.alterDeviceIdCount = 0;
    mockSchemaState.indexCreates = [];

    // Filas preexistentes en cola en memoria (sin device_id físico hasta migrar).
    const now = "2026-07-15T10:00:00Z";
    getSqliteRows().push(
      {
        local_id: 1,
        event_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
        vehicle_id: "VH-001",
        device_id: "",
        driver_id: null,
        timestamp: now,
        latitude: 1,
        longitude: 1,
        speed_kmh: 10,
        fuel_level_percent: null,
        battery_percent: null,
        source: "gps",
        status: "pending",
        retry_count: 0,
        next_attempt_at: null,
        last_attempt_at: null,
        last_error: null,
        locked_at: null,
        synced_at: null,
        created_at: now,
      } as never,
      {
        local_id: 2,
        event_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2",
        vehicle_id: "Unidad Sur",
        device_id: "",
        driver_id: null,
        timestamp: now,
        latitude: 2,
        longitude: 2,
        speed_kmh: 20,
        fuel_level_percent: null,
        battery_percent: null,
        source: "gps",
        status: "pending",
        retry_count: 0,
        next_attempt_at: null,
        last_attempt_at: null,
        last_error: null,
        locked_at: null,
        synced_at: null,
        created_at: now,
      } as never,
      {
        local_id: 3,
        event_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa3",
        vehicle_id: "VH-002",
        device_id: OTHER_UUID,
        driver_id: null,
        timestamp: now,
        latitude: 3,
        longitude: 3,
        speed_kmh: 30,
        fuel_level_percent: null,
        battery_percent: null,
        source: "gps",
        status: "pending",
        retry_count: 0,
        next_attempt_at: null,
        last_attempt_at: null,
        last_error: null,
        locked_at: null,
        synced_at: null,
        created_at: now,
      } as never,
      {
        local_id: 4,
        event_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa4",
        vehicle_id: "VH-003",
        device_id: "",
        driver_id: null,
        timestamp: now,
        latitude: 4,
        longitude: 4,
        speed_kmh: 40,
        fuel_level_percent: null,
        battery_percent: null,
        source: "gps",
        status: "synced",
        retry_count: 0,
        next_attempt_at: null,
        last_attempt_at: null,
        last_error: null,
        locked_at: null,
        synced_at: now,
        created_at: now,
      } as never,
      {
        local_id: 5,
        event_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa5",
        vehicle_id: "VH-004",
        device_id: "",
        driver_id: null,
        timestamp: now,
        latitude: 5,
        longitude: 5,
        speed_kmh: 50,
        fuel_level_percent: null,
        battery_percent: null,
        source: "gps",
        status: "permanent_failure",
        retry_count: 5,
        next_attempt_at: null,
        last_attempt_at: null,
        last_error: "422",
        locked_at: null,
        synced_at: null,
        created_at: now,
      } as never,
    );
    setSqliteMemoryNextLocalId(6);
  });

  it("agrega device_id, conserva filas e índices tras migrar", async () => {
    expect(mockSchemaState.columns.has("device_id")).toBe(false);
    const beforeCount = getSqliteRows().length;
    expect(beforeCount).toBe(5);

    await countPendingEvents();

    expect(mockSchemaState.columns.has("device_id")).toBe(true);
    expect(mockSchemaState.alterDeviceIdCount).toBe(1);
    expect(mockSchemaState.schemaMetaVersion).toBe(String(SCHEMA_VERSION));
    expect(mockSchemaState.userVersion).toBe(SCHEMA_VERSION);
    expect(mockSchemaState.indexCreates.some((s) => s.includes("device_id"))).toBe(true);
    expect(getSqliteRows()).toHaveLength(beforeCount);

    const migration = await migratePendingEventsToDeviceId(LOCAL_UUID);
    expect(migration.migrated).toBeGreaterThanOrEqual(2);
    expect(migration.conflicts.some((c) => c.storedDeviceId === OTHER_UUID)).toBe(true);

    const synced = getSqliteRows().find((r) => r.event_id.endsWith("aaa4"));
    const failed = getSqliteRows().find((r) => r.event_id.endsWith("aaa5"));
    expect(synced?.status).toBe("synced");
    expect(synced?.device_id === "" || synced?.device_id == null || !synced?.device_id).toBe(true);
    expect(failed?.status).toBe("permanent_failure");

    await enqueueEvent(
      {
        eventId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        deviceId: LOCAL_UUID,
        driverId: "DRV",
        timestamp: "2026-07-15T12:00:00Z",
        latitude: 1,
        longitude: 2,
        speedKmh: 3,
        fuelLevelPercent: null,
        batteryPercent: null,
      },
      "simulated",
    );
    expect(getOfflineQueueInitStatsForTests().migrateCount).toBe(1);

    resetOfflineQueueForTests();
    await countPendingEvents();
    expect(mockSchemaState.alterDeviceIdCount).toBe(1);
  });
});
