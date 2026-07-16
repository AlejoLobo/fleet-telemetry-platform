/** Tipos de datos del dominio de telemetría de flotas. */

/** Tipo de vehículo reconocido por el backend y la UI. */
export type VehicleType = "car" | "motorcycle" | "van" | "truck" | "bus" | "pickup";

/** Estado actual de un vehículo en la flota. */
export type VehicleStatus = {
  deviceId: string;
  vehicleName: string;
  vehicleType: VehicleType;
  status: "online" | "offline" | string;
  lastSeenAt: string | null;
  lastEventId?: string | null;
  statusEvaluatedAt?: string | null;
  lastSpeedKmh: number | null;
  lastLatitude: number | null;
  lastLongitude: number | null;
  headingDegrees?: number | null;
  lastLocationSource?: string | null;
  /** Identificador/nombre del conductor asociado al último reporte. */
  driverId?: string | null;
};

/**
 * Parche SSE/parcial: el estado normalizado más la presencia de campos de identidad.
 * `hasVehicleType` es true solo si el payload traía un tipo canónico válido.
 */
export type NormalizedVehiclePatch = {
  vehicle: VehicleStatus;
  hasVehicleType: boolean;
};

/** Alerta operativa de un vehículo. */
export type FleetAlert = {
  alertId: string;
  deviceId: string;
  alertType: string;
  severity: string;
  message: string;
  createdAt: string;
  isAcknowledged: boolean;
};

/** Evento de telemetría GPS y sensores. */
export type TelemetryEvent = {
  eventId: string;
  deviceId: string;
  driverId: string | null;
  timestamp: string;
  latitude: number;
  longitude: number;
  speedKmh: number;
  fuelLevelPercent: number | null;
  batteryPercent: number | null;
  locationSource?: string | null;
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
