// Tipos de datos para eventos de telemetría y sincronización

// Payload enviado a la API
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
};

// Evento almacenado en la cola local SQLite
export type QueuedTelemetryEvent = TelemetryEventPayload & {
  localId: number;
  status: "pending" | "synced" | "failed";
  createdAt: string;
};

// Lectura de ubicación del GPS o simulada
export type LocationReading = {
  latitude: number;
  longitude: number;
  speedKmh: number;
  source: "gps" | "simulated";
};

// Estado de conexión de red
export type NetworkStatus = "online" | "offline" | "unknown";

// Resultado de una operación de sincronización
export type SyncResult = {
  synced: number;
  failed: number;
  remaining: number;
};
