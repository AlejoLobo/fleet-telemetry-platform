import type { AiQueryResponse, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { computeBearingDegrees, moveByBearing } from "@/lib/geo-bearing";

const BOGOTA_BOUNDS = {
  latMin: 4.55,
  latMax: 4.75,
  lngMin: -74.12,
  lngMax: -74.0,
};

const VEHICLE_NAMES = [
  "Camión reparto",
  "Furgoneta urbana",
  "Van refrigerada",
  "Camioneta ligera",
  "Moto carga",
  "Trailer corto",
];

const ALERT_TYPES = [
  { type: "overspeed", severity: "critical" as const, message: (id: string, v: number) =>
    `El vehículo ${id} superó el límite de velocidad: ${v.toFixed(1)} km/h` },
  { type: "low_fuel", severity: "warning" as const, message: (id: string, v: number) =>
    `El vehículo ${id} tiene combustible bajo: ${v.toFixed(1)}%` },
  { type: "low_battery", severity: "warning" as const, message: (id: string, v: number) =>
    `El vehículo ${id} tiene batería baja: ${v.toFixed(1)}%` },
];

export type MockFleetDataset = {
  vehicles: VehicleStatus[];
  alerts: FleetAlert[];
  telemetryByVehicle: Record<string, TelemetryEvent[]>;
};

let cachedDataset: MockFleetDataset | null = null;

function randomBetween(min: number, max: number): number {
  return min + Math.random() * (max - min);
}

function randomInt(min: number, max: number): number {
  return Math.floor(randomBetween(min, max + 1));
}

function randomId(): string {
  return crypto.randomUUID();
}

function randomCoord(): { lat: number; lng: number } {
  return {
    lat: Math.round(randomBetween(BOGOTA_BOUNDS.latMin, BOGOTA_BOUNDS.latMax) * 1e5) / 1e5,
    lng: Math.round(randomBetween(BOGOTA_BOUNDS.lngMin, BOGOTA_BOUNDS.lngMax) * 1e5) / 1e5,
  };
}

function distanceMeters(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = 6_371_000;
  const dLat = ((lat2 - lat1) * Math.PI) / 180;
  const dLng = ((lng2 - lng1) * Math.PI) / 180;
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos((lat1 * Math.PI) / 180) *
      Math.cos((lat2 * Math.PI) / 180) *
      Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}

function randomDistinctCoords(count: number, minDistanceM = 800): { lat: number; lng: number }[] {
  const coords: { lat: number; lng: number }[] = [];
  let attempts = 0;

  while (coords.length < count && attempts < count * 40) {
    attempts += 1;
    const candidate = randomCoord();
    const tooClose = coords.some(
      (c) => distanceMeters(c.lat, c.lng, candidate.lat, candidate.lng) < minDistanceM,
    );
    if (!tooClose) coords.push(candidate);
  }

  while (coords.length < count) {
    coords.push(randomCoord());
  }

  return coords;
}

function generateVehicleBundle(
  index: number,
  coord: { lat: number; lng: number },
): { vehicle: VehicleStatus; events: TelemetryEvent[] } {
  const id = `VH-${String(index + 1).padStart(3, "0")}`;
  const online = Math.random() > 0.28;
  const minutesAgo = randomInt(1, online ? 8 : 45);
  const travelHeading = randomBetween(0, 360);
  const eventCount = randomInt(6, 14);
  const events: TelemetryEvent[] = [];

  let lat = coord.lat;
  let lng = coord.lng;

  for (let i = 0; i < eventCount; i++) {
    const minutes = minutesAgo + i * randomInt(2, 12);
    events.push({
      eventId: randomId(),
      vehicleId: id,
      driverId: `DRV-${id.replace("VH-", "")}`,
      timestamp: new Date(Date.now() - minutes * 60_000).toISOString(),
      latitude: lat,
      longitude: lng,
      speedKmh: Math.round((online ? randomBetween(8, 110) : randomBetween(0, 12)) * 10) / 10,
      fuelLevelPercent: Math.round(randomBetween(8, 98) * 10) / 10,
      batteryPercent: Math.round(randomBetween(12, 100) * 10) / 10,
    });

    const previous = moveByBearing(lat, lng, travelHeading + 180, randomBetween(90, 280));
    lat = previous.lat;
    lng = previous.lng;
  }

  events.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());

  const latest = events[0];
  const previous = events[1];
  const headingDegrees =
    previous != null
      ? computeBearingDegrees(previous.latitude, previous.longitude, latest.latitude, latest.longitude)
      : travelHeading;

  const vehicle: VehicleStatus = {
    vehicleId: id,
    name: `${VEHICLE_NAMES[index % VEHICLE_NAMES.length]} ${String(index + 1).padStart(2, "0")}`,
    status: online ? "online" : "offline",
    lastSeenAt: latest.timestamp,
    lastSpeedKmh: latest.speedKmh,
    lastLatitude: latest.latitude,
    lastLongitude: latest.longitude,
    headingDegrees: Math.round(headingDegrees * 10) / 10,
  };

  return { vehicle, events };
}

function generateAlerts(vehicles: VehicleStatus[]): FleetAlert[] {
  const alerts: FleetAlert[] = [];
  const alertCount = randomInt(1, Math.min(4, vehicles.length));

  for (let i = 0; i < alertCount; i++) {
    const vehicle = vehicles[randomInt(0, vehicles.length - 1)];
    const template = ALERT_TYPES[randomInt(0, ALERT_TYPES.length - 1)];
    const value =
      template.type === "overspeed"
        ? randomBetween(125, 145)
        : randomBetween(5, 18);

    alerts.push({
      alertId: randomId(),
      vehicleId: vehicle.vehicleId,
      alertType: template.type,
      severity: template.severity,
      message: template.message(vehicle.vehicleId, value),
      createdAt: new Date(Date.now() - randomInt(1, 120) * 60_000).toISOString(),
      isAcknowledged: false,
    });
  }

  return alerts;
}

function generateTelemetryBundles(
  bundles: { vehicle: VehicleStatus; events: TelemetryEvent[] }[],
): Record<string, TelemetryEvent[]> {
  const byVehicle: Record<string, TelemetryEvent[]> = {};
  for (const bundle of bundles) {
    byVehicle[bundle.vehicle.vehicleId] = bundle.events;
  }
  return byVehicle;
}

export function generateMockFleetDataset(vehicleCount?: number): MockFleetDataset {
  const count = vehicleCount ?? randomInt(6, 8);
  const coords = randomDistinctCoords(count);
  const bundles = coords.map((coord, index) => generateVehicleBundle(index, coord));
  const vehicles = bundles.map((b) => b.vehicle);
  const alerts = generateAlerts(vehicles);
  const telemetryByVehicle = generateTelemetryBundles(bundles);

  return { vehicles, alerts, telemetryByVehicle };
}

export function refreshMockDataset(vehicleCount?: number): MockFleetDataset {
  cachedDataset = generateMockFleetDataset(vehicleCount);
  return cachedDataset;
}

export function getMockDataset(): MockFleetDataset {
  if (!cachedDataset) {
    cachedDataset = generateMockFleetDataset();
  }
  return cachedDataset;
}

export function getMockTelemetry(vehicleId: string): TelemetryEvent[] {
  return getMockDataset().telemetryByVehicle[vehicleId] ?? [];
}

export function generateMockAiResponse(): AiQueryResponse {
  const { vehicles, alerts } = getMockDataset();
  const online = vehicles.filter((v) => v.status === "online").length;
  const critical = alerts.filter((a) => a.severity === "critical").length;

  return {
    answer: `Hay ${critical} alerta(s) crítica(s) y ${alerts.length} alerta(s) abiertas. ${online} vehículos en línea de ${vehicles.length} en la flota.`,
    sources: ["GetFleetOverview", "GetVehiclesWithCriticalAlerts"],
  };
}
