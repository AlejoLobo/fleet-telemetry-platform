/** Generador de datos sintéticos para modo demostración. */
import type { AiQueryResponse, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { computeBearingDegrees, moveByBearing } from "@/lib/geo-bearing";
import {
  BOGOTA_ZONES,
  randomOnlineFlag,
  randomPointInZone,
  randomTelemetryTimestamp,
  zoneForVehicleIndex,
} from "@/lib/bogota-zones";
import { formatVehicleDisplayName } from "@/lib/labels";
import { getE2eSeed, isE2eTestMode } from "@/lib/e2e-test-mode";
import {
  createSeededRandom,
  randomBetween,
  randomInt,
  randomUuid,
  type RandomSource,
} from "@/lib/seeded-random";

/** Datos sintéticos para el modo demostración del dashboard (sin backend). */

const VEHICLE_NAMES = [
  "Camión reparto",
  "Furgoneta urbana",
  "Van refrigerada",
  "Camioneta ligera",
  "Moto carga",
  "Trailer corto",
];

const ALERT_TYPES = [
  {
    type: "overspeed",
    severity: "critical" as const,
    message: (label: string, v: number) =>
      `El vehículo ${label} superó el límite de velocidad: ${v.toFixed(1)} km/h`,
  },
  {
    type: "low_fuel",
    severity: "warning" as const,
    message: (label: string, v: number) =>
      `El vehículo ${label} tiene combustible bajo: ${v.toFixed(1)}%`,
  },
  {
    type: "low_battery",
    severity: "warning" as const,
    message: (label: string, v: number) =>
      `El vehículo ${label} tiene batería baja: ${v.toFixed(1)}%`,
  },
];

/** Conjunto completo de datos mock (vehículos, alertas, telemetría). */
export type MockFleetDataset = {
  vehicles: VehicleStatus[];
  alerts: FleetAlert[];
  telemetryByDevice: Record<string, TelemetryEvent[]>;
};

let cachedDataset: MockFleetDataset | null = null;
let demoRefreshSequence = 0;

function resolveRandomSource(): RandomSource {
  if (!isE2eTestMode()) return Math.random;
  return createSeededRandom((getE2eSeed() + demoRefreshSequence) >>> 0);
}

/** Reinicia caché y secuencia demo; solo para pruebas automatizadas. */
export function resetMockDatasetForTests(): void {
  cachedDataset = null;
  demoRefreshSequence = 0;
}

/** Secuencia de regeneración demo; solo para aserciones en pruebas. */
export function getDemoRefreshSequenceForTests(): number {
  return demoRefreshSequence;
}

/** UUID determinístico por índice para mocks reproducibles. */
export function mockDeviceId(index: number): string {
  return `00000000-0000-4000-8000-${String(index + 1).padStart(12, "0")}`;
}

function randomCoord(random: RandomSource): { lat: number; lng: number } {
  const zone = BOGOTA_ZONES[randomInt(0, BOGOTA_ZONES.length - 1, random)];
  return randomPointInZone(zone, random);
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

/** Genera coordenadas separadas por zona de Bogotá. */
function randomDistinctCoords(
  count: number,
  random: RandomSource,
  minDistanceM = 1200,
): { lat: number; lng: number }[] {
  const coords: { lat: number; lng: number }[] = [];

  for (let i = 0; i < Math.min(count, BOGOTA_ZONES.length); i++) {
    coords.push(randomPointInZone(zoneForVehicleIndex(i), random));
  }

  let attempts = 0;
  while (coords.length < count && attempts < count * 40) {
    attempts += 1;
    const candidate = randomCoord(random);
    const tooClose = coords.some(
      (c) => distanceMeters(c.lat, c.lng, candidate.lat, candidate.lng) < minDistanceM,
    );
    if (!tooClose) coords.push(candidate);
  }

  while (coords.length < count) {
    coords.push(randomCoord(random));
  }

  return coords;
}

/** Crea un vehículo con su historial de telemetría. */
function generateVehicleBundle(
  index: number,
  coord: { lat: number; lng: number },
  random: RandomSource,
  sequence: number,
): { vehicle: VehicleStatus; events: TelemetryEvent[] } {
  const deviceId = mockDeviceId(index);
  const zone = zoneForVehicleIndex(index);
  const online = isE2eTestMode() ? true : randomOnlineFlag(random);
  const travelHeading = randomBetween(0, 360, random);
  const eventCount = isE2eTestMode() ? 8 : randomInt(6, 14, random);
  const events: TelemetryEvent[] = [];

  let lat = coord.lat;
  let lng = coord.lng;

  for (let i = 0; i < eventCount; i++) {
    const isLatest = i === 0;
    const eventOnline = isLatest ? online : random() > 0.35;
    const speedKmh =
      isE2eTestMode() && isLatest && index === 0
        ? 20 + sequence * 5
        : Math.round((eventOnline ? randomBetween(8, 115, random) : randomBetween(0, 15, random)) * 10) /
          10;

    events.push({
      eventId: isE2eTestMode() ? randomUuid(random) : crypto.randomUUID(),
      deviceId,
      driverId: `DRV-${String(index + 1).padStart(3, "0")}`,
      timestamp: isLatest
        ? randomTelemetryTimestamp(online, random)
        : randomTelemetryTimestamp(eventOnline, random),
      latitude: lat,
      longitude: lng,
      speedKmh,
      fuelLevelPercent: Math.round(randomBetween(8, 98, random) * 10) / 10,
      batteryPercent: Math.round(randomBetween(12, 100, random) * 10) / 10,
    });

    const previous = moveByBearing(
      lat,
      lng,
      travelHeading + randomBetween(-35, 35, random),
      randomBetween(120, 350, random),
    );
    lat = previous.lat;
    lng = previous.lng;
  }

  events.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());

  // En E2E, velocidad del vehículo 0 es determinista por secuencia (tras ordenar).
  if (isE2eTestMode() && index === 0 && events[0]) {
    events[0].speedKmh = 20 + sequence * 5;
  }

  const latest = events[0];
  const previous = events[1];
  const headingDegrees =
    previous != null
      ? computeBearingDegrees(previous.latitude, previous.longitude, latest.latitude, latest.longitude)
      : travelHeading;

  const vehicle: VehicleStatus = {
    deviceId,
    vehicleName: `${VEHICLE_NAMES[index % VEHICLE_NAMES.length]} · ${zone.name}`,
    status: online ? "online" : "offline",
    lastSeenAt: latest.timestamp,
    lastSpeedKmh: latest.speedKmh,
    lastLatitude: latest.latitude,
    lastLongitude: latest.longitude,
    headingDegrees: Math.round(headingDegrees * 10) / 10,
  };

  return { vehicle, events };
}

/** Genera alertas aleatorias para la flota mock. */
function generateAlerts(vehicles: VehicleStatus[], random: RandomSource): FleetAlert[] {
  const alerts: FleetAlert[] = [];
  const alertCount = randomInt(1, Math.min(4, vehicles.length), random);

  for (let i = 0; i < alertCount; i++) {
    const vehicle = vehicles[randomInt(0, vehicles.length - 1, random)];
    const template = ALERT_TYPES[randomInt(0, ALERT_TYPES.length - 1, random)];
    const value =
      template.type === "overspeed"
        ? randomBetween(125, 145, random)
        : randomBetween(5, 18, random);
    const displayLabel = formatVehicleDisplayName(vehicle);

    alerts.push({
      alertId: isE2eTestMode() ? randomUuid(random) : crypto.randomUUID(),
      deviceId: vehicle.deviceId,
      alertType: template.type,
      severity: template.severity,
      message: template.message(displayLabel, value),
      createdAt: new Date(Date.now() - randomInt(1, 120, random) * 60_000).toISOString(),
      isAcknowledged: false,
    });
  }

  return alerts;
}

function generateTelemetryBundles(
  bundles: { vehicle: VehicleStatus; events: TelemetryEvent[] }[],
): Record<string, TelemetryEvent[]> {
  const byDevice: Record<string, TelemetryEvent[]> = {};
  for (const bundle of bundles) {
    byDevice[bundle.vehicle.deviceId] = bundle.events;
  }
  return byDevice;
}

/** Genera un dataset completo de flota simulada. */
export function generateMockFleetDataset(vehicleCount?: number): MockFleetDataset {
  const random = resolveRandomSource();
  const sequence = demoRefreshSequence;
  const count = vehicleCount ?? (isE2eTestMode() ? 10 : randomInt(8, 12, random));
  const coords = randomDistinctCoords(count, random);
  const bundles = coords.map((coord, index) =>
    generateVehicleBundle(index, coord, random, sequence),
  );
  const vehicles = bundles.map((b) => b.vehicle);
  const alerts = generateAlerts(vehicles, random);
  const telemetryByDevice = generateTelemetryBundles(bundles);

  return { vehicles, alerts, telemetryByDevice };
}

/** Regenera y cachea un nuevo dataset demo. */
export function refreshMockDataset(vehicleCount?: number): MockFleetDataset {
  demoRefreshSequence += 1;
  cachedDataset = generateMockFleetDataset(vehicleCount);
  return cachedDataset;
}

/** Obtiene el dataset mock cacheado o genera uno nuevo. */
export function getMockDataset(): MockFleetDataset {
  if (!cachedDataset) {
    // Primera carga: secuencia 0 → se incrementa al refresh; aquí usamos 0 sin incrementar.
    cachedDataset = generateMockFleetDataset();
  }
  return cachedDataset;
}

export function getMockTelemetry(deviceId: string): TelemetryEvent[] {
  return getMockDataset().telemetryByDevice[deviceId] ?? [];
}

/** Respuesta simulada del agente IA para modo demo. */
export function generateMockAiResponse(): AiQueryResponse {
  const { vehicles, alerts } = getMockDataset();
  const online = vehicles.filter((v) => v.status === "online").length;
  const critical = alerts.filter((a) => a.severity === "critical").length;

  return {
    answer: `Hay ${critical} alerta(s) crítica(s) y ${alerts.length} alerta(s) abiertas. ${online} vehículos en línea de ${vehicles.length} en la flota.`,
    sources: ["GetFleetOverview", "GetVehiclesWithCriticalAlerts"],
  };
}
