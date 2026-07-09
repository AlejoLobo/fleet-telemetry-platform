export type VehicleStatus = {
  vehicleId: string;
  name: string;
  status: "online" | "offline" | string;
  lastSeenAt: string | null;
  lastSpeedKmh: number | null;
  lastLatitude: number | null;
  lastLongitude: number | null;
  headingDegrees?: number | null;
};

export type FleetAlert = {
  alertId: string;
  vehicleId: string;
  alertType: string;
  severity: string;
  message: string;
  createdAt: string;
  isAcknowledged: boolean;
};

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

export type AiQueryResponse = {
  answer: string;
  sources: string[];
};

export type SseConnectionState = "connected" | "reconnecting" | "disconnected";

export type AnalyticsSummary = {
  averageSpeedKmh: number;
  activeVehicles: number;
  totalVehicles: number;
  openAlerts: number;
  source: string;
};
