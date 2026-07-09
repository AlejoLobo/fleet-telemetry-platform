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

export type QueuedTelemetryEvent = TelemetryEventPayload & {
  localId: number;
  status: "pending" | "synced" | "failed";
  createdAt: string;
};

export type LocationReading = {
  latitude: number;
  longitude: number;
  speedKmh: number;
  source: "gps" | "simulated";
};

export type NetworkStatus = "online" | "offline" | "unknown";

export type SyncResult = {
  synced: number;
  failed: number;
  remaining: number;
};
