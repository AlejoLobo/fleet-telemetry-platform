/** Zonas geográficas de Bogotá para datos de prueba. */
export const BOGOTA_ZONES = [
  { name: "Chapinero", lat: 4.648, lng: -74.063, spread: 0.018 },
  { name: "Usaquén", lat: 4.711, lng: -74.032, spread: 0.015 },
  { name: "Suba", lat: 4.737, lng: -74.082, spread: 0.02 },
  { name: "Kennedy", lat: 4.628, lng: -74.152, spread: 0.022 },
  { name: "Centro", lat: 4.598, lng: -74.075, spread: 0.012 },
  { name: "Engativá", lat: 4.702, lng: -74.108, spread: 0.016 },
  { name: "Teusaquillo", lat: 4.628, lng: -74.09, spread: 0.014 },
  { name: "Fontibón", lat: 4.669, lng: -74.145, spread: 0.015 },
  { name: "San Cristóbal", lat: 4.568, lng: -74.085, spread: 0.018 },
  { name: "Bosa", lat: 4.612, lng: -74.195, spread: 0.02 },
] as const;

export type BogotaZone = (typeof BOGOTA_ZONES)[number];

/** Punto aleatorio dentro de una zona (no todos en el mismo sitio). */
export function randomPointInZone(zone: BogotaZone): { lat: number; lng: number } {
  const angle = Math.random() * Math.PI * 2;
  const radius = Math.random() * zone.spread;
  return {
    lat: Math.round((zone.lat + Math.cos(angle) * radius) * 1e5) / 1e5,
    lng: Math.round((zone.lng + Math.sin(angle) * radius) * 1e5) / 1e5,
  };
}

/** ~62% online, resto offline (variado, no todo igual). */
export function randomOnlineFlag(): boolean {
  return Math.random() < 0.62;
}

/** Timestamp reciente (online) o antiguo (offline). */
export function randomTelemetryTimestamp(online: boolean): string {
  if (online) {
    const secondsAgo = Math.floor(Math.random() * 240); // 0–4 min
    return new Date(Date.now() - secondsAgo * 1000).toISOString();
  }
  const minutesAgo = 8 + Math.floor(Math.random() * 52); // 8–60 min
  return new Date(Date.now() - minutesAgo * 60_000).toISOString();
}

/** Obtiene la zona según el índice del vehículo. */
export function zoneForVehicleIndex(index: number): BogotaZone {
  return BOGOTA_ZONES[index % BOGOTA_ZONES.length];
}

/** Obtiene la zona según el ID del vehículo (VH-001, etc.). */
export function zoneForVehicleId(vehicleId: string): BogotaZone {
  const match = /VH-(\d+)/i.exec(vehicleId);
  const num = match ? Number.parseInt(match[1], 10) : vehicleId.length;
  return BOGOTA_ZONES[num % BOGOTA_ZONES.length];
}
