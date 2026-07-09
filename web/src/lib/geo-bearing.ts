/** Utilidades geográficas: rumbo, desplazamiento y normalización. */
import type { VehicleStatus } from "@/types/fleet";

/** Rumbo en grados (0=norte, 90=este) desde (lat1,lng1) hacia (lat2,lng2) */
export function computeBearingDegrees(
  lat1: number,
  lng1: number,
  lat2: number,
  lng2: number,
): number {
  const φ1 = (lat1 * Math.PI) / 180;
  const φ2 = (lat2 * Math.PI) / 180;
  const Δλ = ((lng2 - lng1) * Math.PI) / 180;

  const y = Math.sin(Δλ) * Math.cos(φ2);
  const x = Math.cos(φ1) * Math.sin(φ2) - Math.sin(φ1) * Math.cos(φ2) * Math.cos(Δλ);
  const θ = Math.atan2(y, x);

  return ((θ * 180) / Math.PI + 360) % 360;
}

/** Desplaza un punto metros en un rumbo dado */
export function moveByBearing(
  lat: number,
  lng: number,
  bearingDeg: number,
  meters: number,
): { lat: number; lng: number } {
  const bearing = (bearingDeg * Math.PI) / 180;
  const δ = meters / 6_371_000;
  const φ1 = (lat * Math.PI) / 180;
  const λ1 = (lng * Math.PI) / 180;

  const φ2 = Math.asin(
    Math.sin(φ1) * Math.cos(δ) + Math.cos(φ1) * Math.sin(δ) * Math.cos(bearing),
  );
  const λ2 =
    λ1 +
    Math.atan2(
      Math.sin(bearing) * Math.sin(δ) * Math.cos(φ1),
      Math.cos(δ) - Math.sin(φ1) * Math.sin(φ2),
    );

  return {
    lat: Math.round(((φ2 * 180) / Math.PI) * 1e5) / 1e5,
    lng: Math.round(((λ2 * 180) / Math.PI) * 1e5) / 1e5,
  };
}

export function normalizeHeading(heading: number | null | undefined): number {
  if (heading == null || Number.isNaN(heading)) return 0;
  return ((heading % 360) + 360) % 360;
}

export function mapApiVehicle(v: VehicleStatus & { lastHeadingDegrees?: number | null }): VehicleStatus {
  return {
    ...v,
    headingDegrees: v.headingDegrees ?? v.lastHeadingDegrees ?? null,
  };
}
