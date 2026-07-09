import * as SQLite from "expo-sqlite";
import type { QueuedTelemetryEvent, TelemetryEventPayload } from "@/types/telemetry";

let dbPromise: Promise<SQLite.SQLiteDatabase> | null = null;

async function getDb(): Promise<SQLite.SQLiteDatabase> {
  if (!dbPromise) {
    dbPromise = SQLite.openDatabaseAsync("fleet_offline.db");
  }
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
      status TEXT NOT NULL DEFAULT 'pending',
      created_at TEXT NOT NULL
    );
  `);
  return db;
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
    status: row.status as QueuedTelemetryEvent["status"],
    createdAt: row.created_at as string,
  };
}

export async function enqueueEvent(event: TelemetryEventPayload): Promise<number> {
  const db = await getDb();
  const result = await db.runAsync(
    `INSERT INTO telemetry_queue (
      event_id, vehicle_id, driver_id, timestamp,
      latitude, longitude, speed_kmh, fuel_level_percent, battery_percent,
      status, created_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending', ?)`,
    event.eventId,
    event.vehicleId,
    event.driverId,
    event.timestamp,
    event.latitude,
    event.longitude,
    event.speedKmh,
    event.fuelLevelPercent,
    event.batteryPercent,
    new Date().toISOString(),
  );
  return result.lastInsertRowId;
}

export async function getPendingEvents(limit = 50): Promise<QueuedTelemetryEvent[]> {
  const db = await getDb();
  const rows = await db.getAllAsync<Record<string, unknown>>(
    `SELECT * FROM telemetry_queue WHERE status = 'pending' ORDER BY local_id ASC LIMIT ?`,
    limit,
  );
  return rows.map(mapRow);
}

export async function markEventsSynced(eventIds: string[]): Promise<void> {
  if (eventIds.length === 0) return;
  const db = await getDb();
  const placeholders = eventIds.map(() => "?").join(", ");
  await db.runAsync(
    `UPDATE telemetry_queue SET status = 'synced' WHERE event_id IN (${placeholders})`,
    ...eventIds,
  );
}

export async function markEventsFailed(eventIds: string[]): Promise<void> {
  if (eventIds.length === 0) return;
  const db = await getDb();
  const placeholders = eventIds.map(() => "?").join(", ");
  await db.runAsync(
    `UPDATE telemetry_queue SET status = 'failed' WHERE event_id IN (${placeholders})`,
    ...eventIds,
  );
}

export async function countPendingEvents(): Promise<number> {
  const db = await getDb();
  const row = await db.getFirstAsync<{ count: number }>(
    `SELECT COUNT(*) as count FROM telemetry_queue WHERE status = 'pending'`,
  );
  return row?.count ?? 0;
}

export async function resetFailedToPending(): Promise<void> {
  const db = await getDb();
  await db.runAsync(`UPDATE telemetry_queue SET status = 'pending' WHERE status = 'failed'`);
}

export function toPayload(event: QueuedTelemetryEvent): TelemetryEventPayload {
  return {
    eventId: event.eventId,
    vehicleId: event.vehicleId,
    driverId: event.driverId,
    timestamp: event.timestamp,
    latitude: event.latitude,
    longitude: event.longitude,
    speedKmh: event.speedKmh,
    fuelLevelPercent: event.fuelLevelPercent,
    batteryPercent: event.batteryPercent,
  };
}
