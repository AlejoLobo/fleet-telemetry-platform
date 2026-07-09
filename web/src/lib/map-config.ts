/** Configuración del mapa Leaflet y cálculo de límites. */
export const MAP_CENTER = { lat: 4.711, lng: -74.072 };
export const MAP_DEFAULT_ZOOM = 12;

/** Tiles OpenStreetMap */
export const OSM_TILE_URL = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png";

export const OSM_TILE_ATTRIBUTION =
  '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>';

/** Calcula centro y zoom según posiciones de vehículos. */
export function getMapBounds(vehicles: { lastLatitude: number | null; lastLongitude: number | null }[]) {
  const coords = vehicles.filter(
    (v) => v.lastLatitude != null && v.lastLongitude != null,
  ) as { lastLatitude: number; lastLongitude: number }[];

  if (coords.length === 0) {
    return { center: MAP_CENTER, zoom: MAP_DEFAULT_ZOOM };
  }

  const lats = coords.map((v) => v.lastLatitude);
  const lngs = coords.map((v) => v.lastLongitude);

  return {
    center: {
      lat: lats.reduce((a, b) => a + b, 0) / lats.length,
      lng: lngs.reduce((a, b) => a + b, 0) / lngs.length,
    },
    zoom: coords.length === 1 ? 14 : MAP_DEFAULT_ZOOM,
  };
}
