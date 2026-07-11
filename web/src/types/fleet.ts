/** Tipos de datos del dominio de telemetría de flotas. */

/** Estado actual de un vehículo en la flota. */
export type VehicleStatus = {
  vehicleId: string;
  name: string;
  status: "online" | "offline" | string;
  lastSeenAt: string | null;
  lastSpeedKmh: number | null;
  lastLatitude: number | null;
  lastLongitude: number | null;
  headingDegrees?: number | null;
  lastLocationSource?: string | null;
};

/** Alerta operativa de un vehículo. */
export type FleetAlert = {
  alertId: string;
  vehicleId: string;
  alertType: string;
  severity: string;
  message: string;
  createdAt: string;
  isAcknowledged: boolean;
};

/** Evento de telemetría GPS y sensores. */
export type TelemetryEvent = {
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

/** Respuesta del agente IA a una consulta. */
export type AiQueryResponse = {
  answer: string;
  sources: string[];
};

/** Estado de la conexión SSE en tiempo real. */
export type SseConnectionState = "connected" | "reconnecting" | "disconnected";

/** Resumen de métricas analíticas del dashboard. */
export type AnalyticsSummary = {
  averageSpeedKmh: number;
  activeVehicles: number;
  totalVehicles: number;
  openAlerts: number;
  source: string;
};
