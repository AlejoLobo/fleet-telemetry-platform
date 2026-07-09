import type { VehicleStatus } from "@/types/fleet";

const EARTH_RADIUS_M = 6_371_000;

function toRadians(deg: number): number {
  return (deg * Math.PI) / 180;
}

function distanceMeters(
  lat1: number,
  lng1: number,
  lat2: number,
  lng2: number,
): number {
  const dLat = toRadians(lat2 - lat1);
  const dLng = toRadians(lng2 - lng1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRadians(lat1)) * Math.cos(toRadians(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * EARTH_RADIUS_M * Math.asin(Math.sqrt(a));
}

/** Evita que dos vehículos queden en el mismo punto tras el snap a calle */
export function spreadDistinctVehiclePositions(
  vehicles: VehicleStatus[],
  minDistanceMeters = 120,
): VehicleStatus[] {
  const result = vehicles.map((v) => ({ ...v }));

  for (let i = 0; i < result.length; i++) {
    const a = result[i];
    if (a.lastLatitude == null || a.lastLongitude == null) continue;

    for (let j = 0; j < i; j++) {
      const b = result[j];
      if (b.lastLatitude == null || b.lastLongitude == null) continue;

      const dist = distanceMeters(
        a.lastLatitude,
        a.lastLongitude,
        b.lastLatitude,
        b.lastLongitude,
      );

      if (dist < minDistanceMeters) {
        const angle = ((i - j) * 72 * Math.PI) / 180;
        const offsetM = minDistanceMeters - dist + 40;
        const dLat = (offsetM / EARTH_RADIUS_M) * (180 / Math.PI);
        const dLng =
          (offsetM / (EARTH_RADIUS_M * Math.cos(toRadians(a.lastLatitude)))) *
          (180 / Math.PI);

        result[i] = {
          ...a,
          lastLatitude: a.lastLatitude + dLat * Math.cos(angle),
          lastLongitude: a.lastLongitude + dLng * Math.sin(angle),
        };
      }
    }
  }

  return result;
}
