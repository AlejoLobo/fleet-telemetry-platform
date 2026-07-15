// Tipos de datos para eventos de telemetría y sincronización

export type TelemetryEventPayload = {
  eventId: string;
  vehicleId: string;
  driverId: string | null;
  timestamp: string;
  latitude: number;
  longitude: number;
  speedKmh: number;
  fuelLevelPercent: number | null;
  batteryPercent: number | null;
  locationSource?: "gps" | "simulated";
  vehicleName?: string | null;
};

export type QueueStatus = "pending" | "processing" | "retry" | "permanent_failure" | "synced";

export type QueuedTelemetryEvent = TelemetryEventPayload & {
  localId: number;
  source: "gps" | "simulated";
  status: QueueStatus;
  retryCount: number;
  nextAttemptAt: string | null;
  lastAttemptAt: string | null;
  lastError: string | null;
  lockedAt: string | null;
  syncedAt: string | null;
  createdAt: string;
};

export type LocationReading = {
  latitude: number;
  longitude: number;
  speedKmh: number;
  source: "gps" | "simulated";
};

export type NetworkStatus = "online" | "offline" | "unknown";

export type SyncStatus =
  | "completed"
  | "offline"
  | "auth_required"
  | "auth_status_error"
  | "forbidden"
  | "deferred"
  | "configuration_error"
  | "failed";

export type SyncResult = {
  /** Eventos sincronizados correctamente en la corrida. */
  synced: number;
  /** Fallos no recuperables del payload (validación). */
  failed: number;
  /** Eventos movidos a retry por errores transitorios. */
  retried: number;
  /** Eventos marcados permanent_failure por datos inválidos. */
  permanentFailures: number;
  remaining: number;
  status: SyncStatus;
  retryAt?: string;
};
