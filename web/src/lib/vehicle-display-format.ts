/** Formato unificado de tooltip (mapa) y card (Estado de flota). */
import type { VehicleStatus } from "@/types/fleet";
import { esVehiculoEnLinea, etiquetaEstadoVehiculo } from "@/lib/labels";
import { vehicleTypeLabel } from "@/lib/vehicle-types";

export function displayVehicleName(vehicle: Pick<VehicleStatus, "vehicleName">): string {
  const name = vehicle.vehicleName?.trim();
  return name && name.length > 0 ? name : "Vehículo";
}

export function formatSpeed(speedKmh: number | null): string {
  if (speedKmh === null) return "—";
  return `${speedKmh.toFixed(0)} km/h`;
}

export function formatLastSeen(lastSeenAt: string | null): string {
  if (!lastSeenAt) return "—";
  return new Date(lastSeenAt).toLocaleTimeString("es-CO", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

export function formatCoords(latitude: number | null, longitude: number | null): string {
  if (latitude == null || longitude == null) return "—";
  return `${latitude.toFixed(5)}, ${longitude.toFixed(5)}`;
}

export function formatDriverName(driverId: string | null | undefined): string {
  const value = driverId?.trim();
  return value && value.length > 0 ? value : "—";
}

/** Línea: velocidad  hora_ultimo_reporte  tipo_de_vehiculo */
export function formatMetricsLine(
  vehicle: Pick<VehicleStatus, "lastSpeedKmh" | "lastSeenAt" | "vehicleType">,
): string {
  return [
    formatSpeed(vehicle.lastSpeedKmh),
    formatLastSeen(vehicle.lastSeenAt),
    vehicleTypeLabel(vehicle.vehicleType),
  ].join("  ");
}

export type VehicleStatusBadgeInfo = {
  label: string;
  online: boolean;
};

export function formatStatusBadge(status: string): VehicleStatusBadgeInfo {
  return {
    label: etiquetaEstadoVehiculo(status),
    online: esVehiculoEnLinea(status),
  };
}

/** Card Estado de flota (sin conductor ni coordenadas). Estado va como badge aparte. */
export function formatFleetStatusCard(vehicle: VehicleStatus): {
  name: string;
  status: VehicleStatusBadgeInfo;
  deviceId: string;
  metrics: string;
} {
  return {
    name: displayVehicleName(vehicle),
    status: formatStatusBadge(vehicle.status),
    deviceId: vehicle.deviceId,
    metrics: formatMetricsLine(vehicle),
  };
}

/** Tooltip / popup del marcador en mapa. Estado va como badge aparte. */
export function formatVehicleTooltip(vehicle: VehicleStatus): {
  name: string;
  status: VehicleStatusBadgeInfo;
  deviceId: string;
  driverName: string;
  metrics: string;
  coordinates: string;
} {
  return {
    name: displayVehicleName(vehicle),
    status: formatStatusBadge(vehicle.status),
    deviceId: vehicle.deviceId,
    driverName: formatDriverName(vehicle.driverId),
    metrics: formatMetricsLine(vehicle),
    coordinates: formatCoords(vehicle.lastLatitude, vehicle.lastLongitude),
  };
}
