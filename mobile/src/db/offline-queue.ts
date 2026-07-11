import * as SQLite from "expo-sqlite";
import type { QueuedTelemetryEvent, TelemetryEventPayload } from "@/types/telemetry";

const SCHEMA_VERSION = 2;
let dbPromise: Promise<SQLite.SQLiteDatabase> | null = null;

async function getDb(): Promise<SQLite.SQLiteDatabase> {
  if (!dbPromise) dbPromise = SQLite.openDatabaseAsync("fleet_offline.db");
  const db = await dbPromise;
  await db.execAsync(`
    CREATE TABLE IF NOT EXISTS telemetry_queue (
      local_id INTEGER PRIMARY KEY AUTOINCREMENT,
      event_id TEXT NOT NULL UNIQUE,
      vehicle_id TEXT NOT NULL,
      driver_id TEXT,
      timestamp TEXT NOT NULL,
      latitude REAL NOT NULL,
      longitude REAL NOT NULL,
      speed_kmh REAL NOT NULL,
      fuel_level_percent REAL,
      battery_percent REAL,
      source TEXT NOT NULL DEFAULT 'gps',
      status TEXT NOT NULL DEFAULT 'pending',
      retry_count INTEGER NOT NULL DEFAULT 0,
      next_attempt_at TEXT,
      last_attempt_at TEXT,
      last_error TEXT,
      locked_at TEXT,
      synced_at TEXT,
      created_at TEXT NOT NULL
    );
    CREATE TABLE IF NOT EXISTS schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
  `);

  const versionRow = await db.getFirstAsync<{ value: string }>(`SELECT value FROM schema_meta WHERE key = 'version'`);
  if (Number(versionRow?.value ?? 1) < SCHEMA_VERSION) {
    await migrateToV2(db);
    await db.runAsync(`INSERT OR REPLACE INTO schema_meta (key, value) VALUES ('version', ?)`, String(SCHEMA_VERSION));
  }
  return db;
}

async function migrateToV2(db: SQLite.SQLiteDatabase): Promise<void> {
  const columns = await db.getAllAsync<{ name: string }>(`PRAGMA table_info(telemetry_queue)`);
  const names = new Set(columns.map((c) => c.name));
  const add = async (sql: string, col: string) => { if (!names.has(col)) await db.execAsync(sql); };
  await add(`ALTER TABLE telemetry_queue ADD COLUMN source TEXT NOT NULL DEFAULT 'gps'`, "source");
  await add(`ALTER TABLE telemetry_queue ADD COLUMN retry_count INTEGER NOT NULL DEFAULT 0`, "retry_count");
  await add(`ALTER TABLE telemetry_queue ADD COLUMN next_attempt_at TEXT`, "next_attempt_at");
  await add(`ALTER TABLE telemetry_queue ADD COLUMN last_attempt_at TEXT`, "last_attempt_at");
  await add(`ALTER TABLE telemetry_queue ADD COLUMN last_error TEXT`, "last_error");
  await add(`ALTER TABLE telemetry_queue ADD COLUMN locked_at TEXT`, "locked_at");
  await add(`ALTER TABLE telemetry_queue ADD COLUMN synced_at TEXT`, "synced_at");
}

function mapRow(row: Record<string, unknown>): QueuedTelemetryEvent {
  return {
    localId: row.local_id as number,
    eventId: row.event_id as string,
    vehicleId: row.vehicle_id as string,
    driverId: (row.driver_id as string | null) ?? null,
    timestamp: row.timestamp as string,
    latitude: row.latitude as number,
    longitude: row.longitude as number,
    speedKmh: row.speed_kmh as number,
    fuelLevelPercent: (row.fuel_level_percent as number | null) ?? null,
    batteryPercent: (row.battery_percent as number | null) ?? null,
    source: (row.source as QueuedTelemetryEvent["source"]) ?? "gps",
    status: row.status as QueuedTelemetryEvent["status"],
    retryCount: (row.retry_count as number) ?? 0,
    nextAttemptAt: (row.next_attempt_at as string | null) ?? null,
    lastAttemptAt: (row.last_attempt_at as string | null) ?? null,
    lastError: (row.last_error as string | null) ?? null,
    lockedAt: (row.locked_at as string | null) ?? null,
    syncedAt: (row.synced_at as string | null) ?? null,
    createdAt: row.created_at as string,
  };
}

export async function enqueueEvent(event: TelemetryEventPayload, source: "gps" | "simulated" = "gps"): Promise<number> {
  const db = await getDb();
  const result = await db.runAsync(
    `INSERT INTO telemetry_queue (event_id, vehicle_id, driver_id, timestamp, latitude, longitude, speed_kmh,
      fuel_level_percent, battery_percent, source, status, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending', ?)`,
    event.eventId, event.vehicleId, event.driverId, event.timestamp, event.latitude, event.longitude,
    event.speedKmh, event.fuelLevelPercent, event.batteryPercent, source, new Date().toISOString(),
  );
  return result.lastInsertRowId;
}

export async function claimNextBatch(limit: number, nowIso: string): Promise<QueuedTelemetryEvent[]> {
  const db = await getDb();
  await recoverStaleLocks(nowIso);

  return db.withTransactionAsync(async () => {
    const rows = await db.getAllAsync<Record<string, unknown>>(
      `SELECT * FROM telemetry_queue WHERE status IN ('pending','retry') AND (next_attempt_at IS NULL OR next_attempt_at <= ?)
       ORDER BY local_id ASC LIMIT ?`,
      nowIso,
      limit,
    );

    if (rows.length === 0) return [];

    const ids = rows.map((r) => r.local_id as number);
    await db.runAsync(
      `UPDATE telemetry_queue SET status='processing', locked_at=? WHERE local_id IN (${ids.map(() => "?").join(",")})`,
      nowIso,
      ...ids,
    );

    return rows.map(mapRow);
  });
}

export async function markEventsSynced(eventIds: string[]): Promise<void> {
  if (!eventIds.length) return;
  const db = await getDb();
  const now = new Date().toISOString();
  await db.runAsync(
    `UPDATE telemetry_queue SET status='synced', synced_at=?, locked_at=NULL, last_error=NULL WHERE event_id IN (${eventIds.map(() => "?").join(",")})`,
    now, ...eventIds);
}

export async function markEventRetry(eventId: string, retryCount: number, nextAttemptAt: string, lastError: string): Promise<void> {
  const db = await getDb();
  await db.runAsync(
    `UPDATE telemetry_queue SET status='retry', retry_count=?, next_attempt_at=?, last_attempt_at=?, last_error=?, locked_at=NULL WHERE event_id=?`,
    retryCount, nextAttemptAt, new Date().toISOString(), lastError, eventId);
}

export async function markEventPermanentFailure(eventId: string, lastError: string): Promise<void> {
  const db = await getDb();
  await db.runAsync(
    `UPDATE telemetry_queue SET status='permanent_failure', last_attempt_at=?, last_error=?, locked_at=NULL WHERE event_id=?`,
    new Date().toISOString(), lastError, eventId);
}

export async function countPendingEvents(): Promise<number> {
  const db = await getDb();
  const row = await db.getFirstAsync<{ count: number }>(
    `SELECT COUNT(*) as count FROM telemetry_queue WHERE status IN ('pending','retry','processing')`);
  return row?.count ?? 0;
}

export async function recoverStaleLocks(nowIso: string, staleMinutes = 5): Promise<void> {
  const db = await getDb();
  const staleBefore = new Date(new Date(nowIso).getTime() - staleMinutes * 60_000).toISOString();
  await db.runAsync(
    `UPDATE telemetry_queue SET status='retry', locked_at=NULL WHERE status='processing' AND locked_at IS NOT NULL AND locked_at < ?`,
    staleBefore);
}

export async function purgeSyncedOlderThan(days: number): Promise<number> {
  const db = await getDb();
  const cutoff = new Date(Date.now() - days * 86_400_000).toISOString();
  return (await db.runAsync(
    `DELETE FROM telemetry_queue WHERE status='synced' AND synced_at IS NOT NULL AND synced_at < ?`, cutoff)).changes;
}

export function toPayload(event: QueuedTelemetryEvent): TelemetryEventPayload {
  return {
    eventId: event.eventId, vehicleId: event.vehicleId, driverId: event.driverId, timestamp: event.timestamp,
    latitude: event.latitude, longitude: event.longitude, speedKmh: event.speedKmh,
    fuelLevelPercent: event.fuelLevelPercent, batteryPercent: event.batteryPercent, locationSource: event.source,
  };
}
