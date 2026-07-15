type QueueRow = {
  local_id: number;
  event_id: string;
  vehicle_id: string;
  device_id: string;
  driver_id: string | null;
  timestamp: string;
  latitude: number;
  longitude: number;
  speed_kmh: number;
  fuel_level_percent: number | null;
  battery_percent: number | null;
  source: string;
  status: string;
  retry_count: number;
  next_attempt_at: string | null;
  last_attempt_at: string | null;
  last_error: string | null;
  locked_at: string | null;
  synced_at: string | null;
  created_at: string;
};

let rows: QueueRow[] = [];
let nextLocalId = 1;
let failNextBatchRetry = false;

export function resetSqliteMemory(): void {
  rows = [];
  nextLocalId = 1;
  failNextBatchRetry = false;
}

export function getSqliteRows(): QueueRow[] {
  return rows;
}

export function setFailNextBatchRetry(value: boolean): void {
  failNextBatchRetry = value;
}

function findByEventId(eventId: string): QueueRow | undefined {
  return rows.find((row) => row.event_id === eventId);
}

function findByLocalId(localId: number): QueueRow | undefined {
  return rows.find((row) => row.local_id === localId);
}

function updateProcessingByEventIds(
  eventIds: string[],
  updater: (row: QueueRow) => void,
): number {
  let changes = 0;
  eventIds.forEach((eventId) => {
    const row = findByEventId(eventId);
    if (row && row.status === "processing") {
      updater(row);
      changes += 1;
    }
  });
  return changes;
}

export function createSqliteMemoryDb() {
  return {
    execAsync: jest.fn(async () => undefined),
    getFirstAsync: jest.fn(async (sql: string, eventId?: string) => {
      if (sql.includes("schema_meta")) return null;
      if (sql.includes("COUNT(*)")) {
        const count = rows.filter((row) => ["pending", "retry", "processing"].includes(row.status)).length;
        return { count };
      }
      if (sql.includes("event_id = ?") && eventId) {
        const row = findByEventId(eventId);
        return row ? (row as unknown as Record<string, unknown>) : null;
      }
      return null;
    }),
    getAllAsync: jest.fn(async (sql: string, ...params: unknown[]) => {
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
      if (sql.includes("status IN ('pending','retry','processing')") && sql.includes("local_id")) {
        return rows
          .filter((row) => ["pending", "retry", "processing"].includes(row.status))
          .map((row) => row as unknown as Record<string, unknown>);
      }
      if (!sql.includes("telemetry_queue")) return [];

      const nowIso = typeof params[0] === "string" ? params[0] : "";
      let filterDeviceId: string | undefined;
      let limit: number | undefined;
      if (sql.includes("lower(device_id)")) {
        filterDeviceId = typeof params[1] === "string" ? params[1] : undefined;
        limit = typeof params[2] === "number" ? params[2] : undefined;
      } else {
        limit = typeof params[1] === "number" ? params[1] : undefined;
      }

      const eligible = rows
        .filter((row) => ["pending", "retry"].includes(row.status))
        .filter((row) => !row.next_attempt_at || row.next_attempt_at <= nowIso)
        .filter((row) =>
          !filterDeviceId
            || (typeof row.device_id === "string"
              && row.device_id.toLowerCase() === filterDeviceId.toLowerCase()),
        )
        .sort((a, b) => a.local_id - b.local_id);
      return (typeof limit === "number" ? eligible.slice(0, limit) : eligible) as unknown as Record<string, unknown>[];
    }),
    runAsync: jest.fn(async (sql: string, ...params: unknown[]) => {
      if (sql.includes("INSERT OR REPLACE INTO schema_meta")) {
        return { changes: 1 };
      }
      if (sql.includes("SET device_id = NULL WHERE local_id")) {
        const localId = params[0] as number;
        const row = findByLocalId(localId);
        if (row) {
          row.device_id = "";
          return { changes: 1 };
        }
        return { changes: 0 };
      }
      if (sql.includes("SET device_id = ?") && sql.includes("vehicle_id = ?") && sql.includes("local_id = ?")) {
        const deviceId = params[0] as string;
        const vehicleId = params[1] as string;
        const localId = params[2] as number;
        const row = findByLocalId(localId);
        if (row) {
          row.device_id = deviceId;
          row.vehicle_id = vehicleId;
          return { changes: 1 };
        }
        return { changes: 0 };
      }
      if (sql.includes("INSERT INTO telemetry_queue")) {
        // event_id, vehicle_id, device_id, driver_id, timestamp, lat, lon, speed, fuel, battery, source, created_at
        const deviceId = params[2] as string;
        const row: QueueRow = {
          local_id: nextLocalId++,
          event_id: params[0] as string,
          vehicle_id: params[1] as string,
          device_id: deviceId,
          driver_id: (params[3] as string | null) ?? null,
          timestamp: params[4] as string,
          latitude: params[5] as number,
          longitude: params[6] as number,
          speed_kmh: params[7] as number,
          fuel_level_percent: (params[8] as number | null) ?? null,
          battery_percent: (params[9] as number | null) ?? null,
          source: params[10] as string,
          status: "pending",
          retry_count: 0,
          next_attempt_at: null,
          last_attempt_at: null,
          last_error: null,
          locked_at: null,
          synced_at: null,
          created_at: params[11] as string,
        };
        rows.push(row);
        return { lastInsertRowId: row.local_id, changes: 1 };
      }

      if (sql.includes("status='processing'") && sql.includes("local_id IN")) {
        const nowIso = params[0] as string;
        const ids = params.slice(1) as number[];
        ids.forEach((id) => {
          const row = findByLocalId(id);
          if (row) {
            row.status = "processing";
            row.locked_at = nowIso;
          }
        });
        return { changes: ids.length };
      }

      if (sql.includes("status='synced'")) {
        const syncedAt = params[0] as string;
        const eventIds = params.slice(1) as string[];
        const changes = updateProcessingByEventIds(eventIds, (row) => {
          row.status = "synced";
          row.synced_at = syncedAt;
          row.locked_at = null;
          row.last_error = null;
        });
        return { changes };
      }

      if (sql.includes("status='permanent_failure'")) {
        const lastAttemptAt = params[0] as string;
        const lastError = params[1] as string;
        const eventId = params[2] as string;
        const changes = updateProcessingByEventIds([eventId], (row) => {
          row.status = "permanent_failure";
          row.last_attempt_at = lastAttemptAt;
          row.last_error = lastError;
          row.locked_at = null;
        });
        return { changes };
      }

      if (sql.includes("status='retry'") && sql.includes("retry_count=retry_count + 1")) {
        if (failNextBatchRetry) throw new Error("simulated batch retry failure");
        const nextAttemptAt = params[0] as string;
        const lastAttemptAt = params[1] as string;
        const lastError = params[2] as string;
        const eventIds = params.slice(3) as string[];
        const changes = updateProcessingByEventIds(eventIds, (row) => {
          row.status = "retry";
          row.retry_count += 1;
          row.next_attempt_at = nextAttemptAt;
          row.last_attempt_at = lastAttemptAt;
          row.last_error = lastError;
          row.locked_at = null;
        });
        return { changes };
      }

      if (sql.includes("status='pending'") && sql.includes("next_attempt_at=?")) {
        const nextAttemptAt = params[0] as string;
        const lastAttemptAt = params[1] as string;
        const lastError = params[2] as string;
        const eventIds = params.slice(3) as string[];
        const changes = updateProcessingByEventIds(eventIds, (row) => {
          row.status = "pending";
          row.next_attempt_at = nextAttemptAt;
          row.last_attempt_at = lastAttemptAt;
          row.last_error = lastError;
          row.locked_at = null;
        });
        return { changes };
      }

      if (sql.includes("status='pending'") && sql.includes("locked_at=NULL")) {
        const lastAttemptAt = params[0] as string;
        const lastError = (params[1] as string | null) ?? null;
        const eventIds = params.slice(2) as string[];
        const changes = updateProcessingByEventIds(eventIds, (row) => {
          row.status = "pending";
          row.last_attempt_at = lastAttemptAt;
          row.last_error = lastError;
          row.locked_at = null;
        });
        return { changes };
      }

      if (sql.includes("DELETE FROM telemetry_queue")) {
        const before = rows.length;
        rows = rows.filter((row) => !(row.status === "synced" && row.synced_at && row.synced_at < (params[0] as string)));
        return { changes: before - rows.length };
      }

      if (sql.includes("status='retry'") && sql.includes("locked_at IS NOT NULL")) {
        const staleBefore = params[0] as string;
        rows.forEach((row) => {
          if (row.status === "processing" && row.locked_at && row.locked_at < staleBefore) {
            row.status = "retry";
            row.locked_at = null;
          }
        });
        return { changes: 1 };
      }

      return { changes: 0 };
    }),
    withTransactionAsync: jest.fn(async (callback: () => Promise<void>) => callback()),
  };
}
