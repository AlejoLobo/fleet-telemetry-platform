/** Normaliza respuestas del backend al formato del frontend. */
import type {
  FleetAlert,
  NormalizedVehiclePatch,
  TelemetryEvent,
  VehicleStatus,
} from "@/types/fleet";
import { normalizeVehicleType, parseVehicleType } from "@/lib/vehicle-types";

export type RawVehicle = Partial<VehicleStatus> & {
  deviceId?: string;
  vehicleName?: string;
  vehicleId?: string;
  name?: string;
  vehicleType?: unknown;
  VehicleType?: unknown;
  DeviceId?: string;
  VehicleId?: string;
  VehicleName?: string;
  Name?: string;
  Status?: string;
  LastSeenAt?: string | null;
  LastEventId?: string | null;
  StatusEvaluatedAt?: string | null;
  LastSpeedKmh?: number | null;
  LastLatitude?: number | null;
  LastLongitude?: number | null;
  lastHeadingDegrees?: number | null;
  LastHeadingDegrees?: number | null;
  LastLocationSource?: string | null;
  driverId?: string | null;
  DriverId?: string | null;
};

type RawTelemetry = Partial<TelemetryEvent> & {
  deviceId?: string;
  vehicleId?: string;
  DeviceId?: string;
  VehicleId?: string;
  DriverId?: string | null;
  Timestamp?: string;
  Latitude?: number;
  Longitude?: number;
  SpeedKmh?: number;
  FuelLevelPercent?: number | null;
  BatteryPercent?: number | null;
  LocationSource?: string | null;
};

type RawAlert = Partial<FleetAlert> & {
  deviceId?: string;
  vehicleId?: string;
  DeviceId?: string;
  VehicleId?: string;
  AlertId?: string;
  AlertType?: string;
  Severity?: string;
  Message?: string;
  CreatedAt?: string;
  IsAcknowledged?: boolean;
};

function rawHadVehicleTypeKey(vehicle: RawVehicle): boolean {
  return Object.prototype.hasOwnProperty.call(vehicle, "vehicleType")
    || Object.prototype.hasOwnProperty.call(vehicle, "VehicleType");
}

function rawVehicleTypeValue(vehicle: RawVehicle): unknown {
  if (Object.prototype.hasOwnProperty.call(vehicle, "vehicleType")) {
    return vehicle.vehicleType;
  }
  if (Object.prototype.hasOwnProperty.call(vehicle, "VehicleType")) {
    return vehicle.VehicleType;
  }
  return undefined;
}

function buildVehicleStatus(
  vehicle: RawVehicle,
  vehicleType: VehicleStatus["vehicleType"],
): VehicleStatus {
  const deviceId =
    vehicle.deviceId ??
    vehicle.DeviceId ??
    vehicle.vehicleId ??
    vehicle.VehicleId ??
    "";
  const vehicleName =
    vehicle.vehicleName ??
    vehicle.VehicleName ??
    vehicle.name ??
    vehicle.Name ??
    "";

  return {
    deviceId,
    vehicleName,
    vehicleType,
    status: vehicle.status ?? vehicle.Status ?? "offline",
    lastSeenAt: vehicle.lastSeenAt ?? vehicle.LastSeenAt ?? null,
    lastEventId: vehicle.lastEventId ?? vehicle.LastEventId ?? null,
    statusEvaluatedAt: vehicle.statusEvaluatedAt ?? vehicle.StatusEvaluatedAt ?? null,
    lastSpeedKmh: vehicle.lastSpeedKmh ?? vehicle.LastSpeedKmh ?? null,
    lastLatitude: vehicle.lastLatitude ?? vehicle.LastLatitude ?? null,
    lastLongitude: vehicle.lastLongitude ?? vehicle.LastLongitude ?? null,
    headingDegrees:
      vehicle.headingDegrees ?? vehicle.lastHeadingDegrees ?? vehicle.LastHeadingDegrees ?? null,
    lastLocationSource:
      vehicle.lastLocationSource ?? vehicle.LastLocationSource ?? null,
    driverId: vehicle.driverId ?? vehicle.DriverId ?? null,
  };
}

/**
 * Snapshot completo: tipo ausente/inválido → car.
 * No adjunta metadatos internos al modelo público.
 */
export function normalizeVehicle(vehicle: RawVehicle): VehicleStatus {
  return buildVehicleStatus(vehicle, normalizeVehicleType(rawVehicleTypeValue(vehicle)));
}

/** Alias explícito para normalización de estado de vehículo. */
export const normalizeVehicleStatus = normalizeVehicle;

export function normalizeVehicles(vehicles: RawVehicle[]): VehicleStatus[] {
  return vehicles.map(normalizeVehicle);
}

/**
 * Parche SSE/parcial: solo marca hasVehicleType cuando el payload trae un tipo canónico válido.
 * Tipo inválido o null no reemplaza el valor previo en el merge.
 */
export function normalizeVehiclePatch(vehicle: RawVehicle): NormalizedVehiclePatch {
  const rawType = rawVehicleTypeValue(vehicle);
  const parsed = parseVehicleType(rawType);
  const hasVehicleType = rawHadVehicleTypeKey(vehicle) && parsed != null;

  return {
    vehicle: buildVehicleStatus(vehicle, parsed ?? "car"),
    hasVehicleType,
  };
}

export function normalizeVehiclePatches(vehicles: RawVehicle[]): NormalizedVehiclePatch[] {
  return vehicles.map(normalizeVehiclePatch);
}

/** Normaliza un evento de telemetría del API. */
export function normalizeTelemetryEvent(event: RawTelemetry): TelemetryEvent {
  return {
    eventId: event.eventId ?? "",
    deviceId:
      event.deviceId ??
      event.DeviceId ??
      event.vehicleId ??
      event.VehicleId ??
      "",
    driverId: event.driverId ?? event.DriverId ?? null,
    timestamp: event.timestamp ?? event.Timestamp ?? "",
    latitude: event.latitude ?? event.Latitude ?? 0,
    longitude: event.longitude ?? event.Longitude ?? 0,
    speedKmh: event.speedKmh ?? event.SpeedKmh ?? 0,
    fuelLevelPercent: event.fuelLevelPercent ?? event.FuelLevelPercent ?? null,
    batteryPercent: event.batteryPercent ?? event.BatteryPercent ?? null,
    locationSource: event.locationSource ?? event.LocationSource ?? null,
  };
}

export function normalizeTelemetryEvents(events: RawTelemetry[]): TelemetryEvent[] {
  return events.map(normalizeTelemetryEvent);
}

/** Normaliza una alerta del API. */
export function normalizeAlert(alert: RawAlert): FleetAlert {
  return {
    alertId: alert.alertId ?? alert.AlertId ?? "",
    deviceId:
      alert.deviceId ??
      alert.DeviceId ??
      alert.vehicleId ??
      alert.VehicleId ??
      "",
    alertType: alert.alertType ?? alert.AlertType ?? "",
    severity: alert.severity ?? alert.Severity ?? "",
    message: alert.message ?? alert.Message ?? "",
    createdAt: alert.createdAt ?? alert.CreatedAt ?? "",
    isAcknowledged: alert.isAcknowledged ?? alert.IsAcknowledged ?? false,
  };
}

export function normalizeAlerts(alerts: RawAlert[]): FleetAlert[] {
  return alerts.map(normalizeAlert);
}
