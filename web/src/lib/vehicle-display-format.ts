/** Formato unificado de tooltip (mapa) y card (Estado de flota). */
import type { VehicleStatus } from "@/types/fleet";
import { etiquetaEstadoVehiculo } from "@/lib/labels";
import { vehicleTypeLabel } from "@/lib/vehicle-types";

export function displayVehicleName(vehicle: Pick<VehicleStatus, "vehicleName">): string {
  const name = vehicle.vehicleName?.trim();
  return name && name.length > 0 ? name : "Vehículo";
}

export function formatVehicleTitleLine(vehicle: Pick<VehicleStatus, "vehicleName" | "status">): string {
  return `${displayVehicleName(vehicle)} (${etiquetaEstadoVehiculo(vehicle.status)})`;
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

/** Card Estado de flota (sin conductor ni coordenadas). */
export function formatFleetStatusCard(vehicle: VehicleStatus): {
  title: string;
  deviceId: string;
  metrics: string;
} {
  return {
    title: formatVehicleTitleLine(vehicle),
    deviceId: vehicle.deviceId,
    metrics: formatMetricsLine(vehicle),
  };
}

/** Tooltip / popup del marcador en mapa. */
export function formatVehicleTooltip(vehicle: VehicleStatus): {
  title: string;
  deviceId: string;
  driverName: string;
  metrics: string;
  coordinates: string;
} {
  return {
    title: formatVehicleTitleLine(vehicle),
    deviceId: vehicle.deviceId,
    driverName: formatDriverName(vehicle.driverId),
    metrics: formatMetricsLine(vehicle),
    coordinates: formatCoords(vehicle.lastLatitude, vehicle.lastLongitude),
  };
}
