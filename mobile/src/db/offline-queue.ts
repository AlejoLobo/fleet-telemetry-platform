import * as SQLite from "expo-sqlite";
import { isValidDeviceId } from "@/services/device-id-store";
import type { QueuedTelemetryEvent, TelemetryEventPayload } from "@/types/telemetry";

/** Versión lógica del esquema (schema_meta + PRAGMA user_version). */
export const SCHEMA_VERSION = 5;

/** Promesa de base ya abierta y migrada (una sola inicialización por proceso). */
let readyDbPromise: Promise<SQLite.SQLiteDatabase> | null = null;

/** Contadores de inicialización; solo para aserciones en pruebas. */
let initOpenCountForTests = 0;
let initMigrateCountForTests = 0;

export class SchemaMigrationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "SchemaMigrationError";
  }
}

/** Reinicia la conexión SQLite; solo para pruebas automatizadas. */
export function resetOfflineQueueForTests(): void {
  readyDbPromise = null;
  initOpenCountForTests = 0;
  initMigrateCountForTests = 0;
}

/** Estadísticas de apertura/migración; solo para pruebas automatizadas. */
export function getOfflineQueueInitStatsForTests(): {
  openCount: number;
  migrateCount: number;
} {
  return {
    openCount: initOpenCountForTests,
    migrateCount: initMigrateCountForTests,
  };
}

async function readColumnNames(db: SQLite.SQLiteDatabase): Promise<Set<string>> {
  const columns = await db.getAllAsync<{ name: string }>(`PRAGMA table_info(telemetry_queue)`);
  return new Set(columns.map((c) => c.name));
}

async function ensureColumn(
  db: SQLite.SQLiteDatabase,
  names: Set<string>,
  column: string,
  ddl: string,
): Promise<void> {
  if (names.has(column)) return;
  await db.execAsync(ddl);
  names.add(column);
}

/**
 * Migración incremental e idempotente.
 * Siempre verifica columnas con PRAGMA table_info (no confía solo en schema_meta).
 */
async function migrateSchema(db: SQLite.SQLiteDatabase): Promise<void> {
  await db.execAsync(`
    CREATE TABLE IF NOT EXISTS telemetry_queue (
      local_id INTEGER PRIMARY KEY AUTOINCREMENT,
      event_id TEXT NOT NULL UNIQUE,
      vehicle_id TEXT NOT NULL,
      device_id TEXT,
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

  const versionRow = await db.getFirstAsync<{ value: string }>(
    `SELECT value FROM schema_meta WHERE key = 'version'`,
  );
  const currentVersion = Number(versionRow?.value ?? 1);

  await db.withTransactionAsync(async () => {
    const names = await readColumnNames(db);

    // v2: reintentos / metadatos de sync
    await ensureColumn(db, names, "source", `ALTER TABLE telemetry_queue ADD COLUMN source TEXT NOT NULL DEFAULT 'gps'`);
    await ensureColumn(db, names, "retry_count", `ALTER TABLE telemetry_queue ADD COLUMN retry_count INTEGER NOT NULL DEFAULT 0`);
    await ensureColumn(db, names, "next_attempt_at", `ALTER TABLE telemetry_queue ADD COLUMN next_attempt_at TEXT`);
    await ensureColumn(db, names, "last_attempt_at", `ALTER TABLE telemetry_queue ADD COLUMN last_attempt_at TEXT`);
    await ensureColumn(db, names, "last_error", `ALTER TABLE telemetry_queue ADD COLUMN last_error TEXT`);
    await ensureColumn(db, names, "locked_at", `ALTER TABLE telemetry_queue ADD COLUMN locked_at TEXT`);
    await ensureColumn(db, names, "synced_at", `ALTER TABLE telemetry_queue ADD COLUMN synced_at TEXT`);

    // v3/v4/v5: device_id (nullable; CREATE IF NOT EXISTS no añade columnas a tablas viejas)
    await ensureColumn(db, names, "device_id", `ALTER TABLE telemetry_queue ADD COLUMN device_id TEXT`);

    // Invalidar device_id no-UUID en activos (backfill ocurre en migratePendingEventsToDeviceId).
    const rows = await db.getAllAsync<Record<string, unknown>>(
      `SELECT local_id, device_id FROM telemetry_queue
       WHERE status IN ('pending','retry','processing')`,
    );
    for (const row of rows) {
      const deviceId = typeof row.device_id === "string" ? row.device_id : "";
      if (isValidDeviceId(deviceId)) continue;
      if (!deviceId) continue;
      await db.runAsync(
        `UPDATE telemetry_queue SET device_id = NULL WHERE local_id = ?`,
        row.local_id as number,
      );
    }

    await db.execAsync(`
      CREATE INDEX IF NOT EXISTS idx_telemetry_queue_status_next
        ON telemetry_queue (status, next_attempt_at);
      CREATE INDEX IF NOT EXISTS idx_telemetry_queue_device_status
        ON telemetry_queue (device_id, status, created_at);
    `);

    const verified = await readColumnNames(db);
    if (!verified.has("device_id")) {
      throw new SchemaMigrationError(
        "Migración SQLite incompleta: telemetry_queue no tiene columna device_id",
      );
    }

    await db.runAsync(
      `INSERT OR REPLACE INTO schema_meta (key, value) VALUES ('version', ?)`,
      String(SCHEMA_VERSION),
    );
    await db.execAsync(`PRAGMA user_version = ${SCHEMA_VERSION}`);
  });

  // Verificación post-commit (también tras instalaciones ya en schema_meta=4 sin columna).
  const finalNames = await readColumnNames(db);
  if (!finalNames.has("device_id")) {
    throw new SchemaMigrationError(
      "telemetry_queue no tiene columna device_id tras migrar el esquema",
    );
  }

  // Si schema_meta decía estar al día pero faltaba la columna, la transacción ya la curó.
  void currentVersion;
}

/**
 * Abre la base y migra el esquema una sola vez.
 * Llamadas concurrentes comparten la misma promesa; un fallo limpia el caché para reintentar.
 */
async function getDb(): Promise<SQLite.SQLiteDatabase> {
  if (!readyDbPromise) {
    readyDbPromise = (async () => {
      initOpenCountForTests += 1;
      const db = await SQLite.openDatabaseAsync("fleet_offline.db");
      initMigrateCountForTests += 1;
      await migrateSchema(db);
      return db;
    })().catch((error) => {
      readyDbPromise = null;
      throw error;
    });
  }

  return readyDbPromise;
}

export type DeviceIdentityConflict = {
  eventId: string;
  storedDeviceId: string;
  currentDeviceId: string;
};

export type DeviceIdMigrationResult = {
  migrated: number;
  unchanged: number;
  conflicts: DeviceIdentityConflict[];
};

/**
 * Migra eventos activos con device_id nulo/vacío/inválido al UUID estable.
 * UUIDs válidos distintos no se sobrescriben: se reportan como conflicto.
 * No modifica synced ni permanent_failure.
 */
export async function migratePendingEventsToDeviceId(deviceId: string): Promise<DeviceIdMigrationResult> {
  const stableId = deviceId.trim();
  if (!isValidDeviceId(stableId)) {
    throw new Error("deviceId inválido para migración de cola");
  }

  const db = await getDb();
  const rows = await db.getAllAsync<Record<string, unknown>>(
    `SELECT local_id, event_id, device_id, vehicle_id FROM telemetry_queue
     WHERE status IN ('pending','retry','processing')`,
  );

  let migrated = 0;
  let unchanged = 0;
  const conflicts: DeviceIdentityConflict[] = [];

  for (const row of rows) {
    const eventId = String(row.event_id ?? "");
    const current = typeof row.device_id === "string" ? row.device_id.trim() : "";

    if (isValidDeviceId(current) && current.toLowerCase() === stableId.toLowerCase()) {
      unchanged += 1;
      continue;
    }

    if (isValidDeviceId(current) && current.toLowerCase() !== stableId.toLowerCase()) {
      conflicts.push({
        eventId,
        storedDeviceId: current,
        currentDeviceId: stableId,
      });
      continue;
    }

    // Nulo, vacío o inválido (VH-001, nombre libre, etc.): migrar al UUID actual.
    await db.runAsync(
      `UPDATE telemetry_queue SET device_id = ?, vehicle_id = ? WHERE local_id = ?`,
      stableId,
      stableId,
      row.local_id as number,
    );
    migrated += 1;
  }

  return { migrated, unchanged, conflicts };
}

function mapRow(row: Record<string, unknown>): QueuedTelemetryEvent {
  const rawDeviceId =
    (typeof row.device_id === "string" && row.device_id.trim())
      ? row.device_id.trim()
      : "";
  const deviceId = isValidDeviceId(rawDeviceId)
    ? rawDeviceId
    : (isValidDeviceId(String(row.vehicle_id ?? ""))
      ? String(row.vehicle_id).trim()
      : rawDeviceId || String(row.vehicle_id ?? ""));

  return {
    localId: row.local_id as number,
    eventId: row.event_id as string,
    deviceId,
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


function logQueueStateConflict(operation: string, expected: number, affected: number): void {
  if (expected !== affected) {
    console.warn(`[offline-queue] ${operation}: conflicto de estado (esperado=${expected}, afectado=${affected})`);
  }
}

export async function enqueueEvent(event: TelemetryEventPayload, source: "gps" | "simulated" = "gps"): Promise<number> {
  const db = await getDb();
  const deviceId = event.deviceId.trim();
  const result = await db.runAsync(
    `INSERT INTO telemetry_queue (event_id, vehicle_id, device_id, driver_id, timestamp, latitude, longitude, speed_kmh,
      fuel_level_percent, battery_percent, source, status, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending', ?)`,
    event.eventId, deviceId, deviceId, event.driverId, event.timestamp, event.latitude, event.longitude,
    event.speedKmh, event.fuelLevelPercent, event.batteryPercent, source, new Date().toISOString(),
  );
  return result.lastInsertRowId;
}

export async function claimNextBatch(
  limit: number,
  nowIso: string,
  deviceId?: string,
): Promise<QueuedTelemetryEvent[]> {
  const db = await getDb();
  await recoverStaleLocks(nowIso);

  let claimed: QueuedTelemetryEvent[] = [];
  const stableId = deviceId?.trim() ?? "";
  const filterByDevice = isValidDeviceId(stableId);

  await db.withTransactionAsync(async () => {
    const rows = filterByDevice
      ? await db.getAllAsync<Record<string, unknown>>(
          `SELECT * FROM telemetry_queue WHERE status IN ('pending','retry')
           AND (next_attempt_at IS NULL OR next_attempt_at <= ?)
           AND lower(device_id) = lower(?)
           ORDER BY local_id ASC LIMIT ?`,
          nowIso,
          stableId,
          limit,
        )
      : await db.getAllAsync<Record<string, unknown>>(
          `SELECT * FROM telemetry_queue WHERE status IN ('pending','retry') AND (next_attempt_at IS NULL OR next_attempt_at <= ?)
           ORDER BY local_id ASC LIMIT ?`,
          nowIso,
          limit,
        );

    if (rows.length === 0) {
      claimed = [];
      return;
    }

    const ids = rows.map((r) => r.local_id as number);
    await db.runAsync(
      `UPDATE telemetry_queue SET status='processing', locked_at=? WHERE local_id IN (${ids.map(() => "?").join(",")})`,
      nowIso,
      ...ids,
    );

    claimed = rows.map(mapRow);
  });

  return claimed;
}

export async function markEventsSynced(eventIds: string[]): Promise<number> {
  if (!eventIds.length) return 0;
  const db = await getDb();
  const now = new Date().toISOString();
  const result = await db.runAsync(
    `UPDATE telemetry_queue SET status='synced', synced_at=?, locked_at=NULL, last_error=NULL
     WHERE status='processing' AND event_id IN (${eventIds.map(() => "?").join(",")})`,
    now, ...eventIds);
  logQueueStateConflict("markEventsSynced", eventIds.length, result.changes);
  return result.changes;
}

export async function markEventRetry(eventId: string, retryCount: number, nextAttemptAt: string, lastError: string): Promise<number> {
  const db = await getDb();
  const result = await db.runAsync(
    `UPDATE telemetry_queue SET status='retry', retry_count=?, next_attempt_at=?, last_attempt_at=?, last_error=?, locked_at=NULL
     WHERE status='processing' AND event_id=?`,
    retryCount, nextAttemptAt, new Date().toISOString(), lastError, eventId);
  logQueueStateConflict("markEventRetry", 1, result.changes);
  return result.changes;
}

export async function markEventPermanentFailure(eventId: string, lastError: string): Promise<number> {
  const db = await getDb();
  const result = await db.runAsync(
    `UPDATE telemetry_queue SET status='permanent_failure', last_attempt_at=?, last_error=?, locked_at=NULL
     WHERE status='processing' AND event_id=?`,
    new Date().toISOString(), lastError, eventId);
  logQueueStateConflict("markEventPermanentFailure", 1, result.changes);
  return result.changes;
}

export async function releaseEventsToPending(eventIds: string[], lastError?: string): Promise<number> {
  if (!eventIds.length) return 0;
  const db = await getDb();
  let affected = 0;
  await db.withTransactionAsync(async () => {
    const result = await db.runAsync(
      `UPDATE telemetry_queue
       SET status='pending', locked_at=NULL, last_attempt_at=?, last_error=?
       WHERE status='processing' AND event_id IN (${eventIds.map(() => "?").join(",")})`,
      new Date().toISOString(),
      lastError ?? null,
      ...eventIds,
    );
    affected = result.changes;
  });
  logQueueStateConflict("releaseEventsToPending", eventIds.length, affected);
  return affected;
}

export async function markBatchRetry(
  eventIds: string[],
  nextAttemptAt: string,
  lastError: string,
  incrementRetry: boolean,
): Promise<number> {
  if (!eventIds.length) return 0;
  const db = await getDb();
  const now = new Date().toISOString();
  let affected = 0;
  await db.withTransactionAsync(async () => {
    if (incrementRetry) {
      const result = await db.runAsync(
        `UPDATE telemetry_queue
         SET status='retry', retry_count=retry_count + 1, next_attempt_at=?, last_attempt_at=?, last_error=?, locked_at=NULL
         WHERE status='processing' AND event_id IN (${eventIds.map(() => "?").join(",")})`,
        nextAttemptAt,
        now,
        lastError,
        ...eventIds,
      );
      affected = result.changes;
      return;
    }
    const result = await db.runAsync(
      `UPDATE telemetry_queue
       SET status='pending', next_attempt_at=?, last_attempt_at=?, last_error=?, locked_at=NULL
       WHERE status='processing' AND event_id IN (${eventIds.map(() => "?").join(",")})`,
      nextAttemptAt,
      now,
      lastError,
      ...eventIds,
    );
    affected = result.changes;
  });
  logQueueStateConflict("markBatchRetry", eventIds.length, affected);
  return affected;
}

/** Reintento transitorio atómico para todo el lote reclamado. */
export async function markClaimedBatchRetryAtomic(
  eventIds: string[],
  nextAttemptAt: string,
  lastError: string,
): Promise<number> {
  return markBatchRetry(eventIds, nextAttemptAt, lastError, true);
}

export async function releaseClaimedEvents(eventIds: string[], lastError?: string): Promise<number> {
  return releaseEventsToPending(eventIds, lastError);
}

export async function getQueueEventByEventId(eventId: string): Promise<QueuedTelemetryEvent | null> {
  const db = await getDb();
  const row = await db.getFirstAsync<Record<string, unknown>>(
    `SELECT * FROM telemetry_queue WHERE event_id = ?`,
    eventId,
  );
  return row ? mapRow(row) : null;
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
    eventId: event.eventId,
    deviceId: event.deviceId,
    driverId: event.driverId,
    timestamp: event.timestamp,
    latitude: event.latitude,
    longitude: event.longitude,
    speedKmh: event.speedKmh,
    fuelLevelPercent: event.fuelLevelPercent,
    batteryPercent: event.batteryPercent,
    locationSource: event.source,
  };
}
