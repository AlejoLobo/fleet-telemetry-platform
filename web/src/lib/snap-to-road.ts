/** Ajuste de coordenadas GPS a vías con OSRM. */
type SnapResult = { lat: number; lng: number };

const snapCache = new Map<string, SnapResult>();

function cacheKey(lat: number, lng: number): string {
  return `${lat.toFixed(5)},${lng.toFixed(5)}`;
}

/** Ajusta un punto GPS al eje vial más cercano (OSRM / OpenStreetMap) */
export async function snapToNearestRoad(lat: number, lng: number): Promise<SnapResult> {
  const key = cacheKey(lat, lng);
  const cached = snapCache.get(key);
  if (cached) return cached;

  try {
    const url = `https://router.project-osrm.org/nearest/v1/driving/${lng},${lat}?number=1`;
    const response = await fetch(url);
    if (!response.ok) return { lat, lng };

    const data = (await response.json()) as {
      code?: string;
      waypoints?: { location: [number, number] }[];
    };

    if (data.code === "Ok" && data.waypoints?.[0]?.location) {
      const [snappedLng, snappedLat] = data.waypoints[0].location;
      const result = { lat: snappedLat, lng: snappedLng };
      snapCache.set(key, result);
      return result;
    }
  } catch {
    /* sin red: usar coordenada original */
  }

  return { lat, lng };
}

/** Ajusta todos los vehículos a la calle más cercana. */
export async function snapVehiclesToRoads<
  T extends { vehicleId: string; lastLatitude: number | null; lastLongitude: number | null },
>(vehicles: T[]): Promise<T[]> {
  const tasks = vehicles.map(async (vehicle) => {
    if (vehicle.lastLatitude == null || vehicle.lastLongitude == null) return vehicle;

    const snapped = await snapToNearestRoad(vehicle.lastLatitude, vehicle.lastLongitude);
    return {
      ...vehicle,
      lastLatitude: snapped.lat,
      lastLongitude: snapped.lng,
    };
  });

  return Promise.all(tasks);
}
