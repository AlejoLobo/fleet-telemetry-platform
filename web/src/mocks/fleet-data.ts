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
import type { VehicleType } from "@/types/fleet";
import { getE2eSeed, isE2eTestMode } from "@/lib/e2e-test-mode";
import {
  createSeededRandom,
  randomBetween,
  randomInt,
  randomUuid,
  type RandomSource,
} from "@/lib/seeded-random";

/** Datos sintéticos para el modo demostración del dashboard (sin backend). */

const DEMO_VEHICLE_NAMES = [
  "VH-001",
  "VH-002",
  "VH-003",
  "VH-004",
  "VH-005",
  "VH-006",
];

const DEMO_VEHICLE_TYPES: VehicleType[] = [
  "truck",
  "van",
  "van",
  "pickup",
  "motorcycle",
  "truck",
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

  const demoIndex = index % DEMO_VEHICLE_NAMES.length;
  const vehicle: VehicleStatus = {
    deviceId,
    vehicleName: DEMO_VEHICLE_NAMES[demoIndex] ?? `VH-${String(index + 1).padStart(3, "0")}`,
    vehicleType: DEMO_VEHICLE_TYPES[demoIndex] ?? "car",
    status: online ? "online" : "offline",
    lastSeenAt: latest.timestamp,
    lastSpeedKmh: latest.speedKmh,
    lastLatitude: latest.latitude,
    lastLongitude: latest.longitude,
    headingDegrees: Math.round(headingDegrees * 10) / 10,
    lastLocationSource: "simulated",
    driverId: latest.driverId,
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

const DEFAULT_DEMO_SPEED_KMH = 80;

/** Interpreta la pregunta demo con las mismas intenciones operativas del backend. */
function parseDemoAiIntent(question: string): {
  kind: "overview" | "critical" | "stopped" | "speed" | "unsupported";
  speedKmh: number;
} {
  const lower = question.trim().toLowerCase();
  const kmhMatch = lower.match(/(\d+(?:[.,]\d+)?)\s*km\s*\/?\s*h/);
  const speedKmh = kmhMatch
    ? Number.parseFloat(kmhMatch[1].replace(",", ".")) || DEFAULT_DEMO_SPEED_KMH
    : DEFAULT_DEMO_SPEED_KMH;
  const mentionsStopped = /deten|parad|stopped|quieto|inmovil|inmóvil|sin mover/.test(lower);
  const mentionsCritical = /crític|critic|grave/.test(lower);
  const mentionsAlerts = /alerta|alert/.test(lower);
  const mentionsSpeed =
    /veloc|rápid|rapido|speed|exceso|por encima/.test(lower) || kmhMatch != null;
  const mentionsOverview = /resumen|overview|flota|cuántos|cuantos/.test(lower);

  if (mentionsStopped) return { kind: "stopped", speedKmh };
  if ((mentionsAlerts && mentionsCritical) || mentionsCritical) return { kind: "critical", speedKmh };
  if (mentionsSpeed) return { kind: "speed", speedKmh };
  if (mentionsAlerts) return { kind: "critical", speedKmh };
  if (mentionsOverview) return { kind: "overview", speedKmh };
  return { kind: "unsupported", speedKmh };
}

function formatDemoVehicleLine(vehicle: VehicleStatus): string {
  const speed =
    vehicle.lastSpeedKmh == null ? "n/d" : `${vehicle.lastSpeedKmh.toFixed(1)} km/h`;
  return `- ${vehicle.vehicleName} (${vehicle.status}, ${speed})`;
}

/** Respuesta simulada del agente IA para modo demo (varía según la pregunta). */
export function generateMockAiResponse(question: string): AiQueryResponse {
  const { vehicles, alerts } = getMockDataset();
  const intent = parseDemoAiIntent(question);

  if (intent.kind === "critical") {
    const critical = alerts.filter((a) => /critical|crític/i.test(a.severity));
    if (critical.length === 0) {
      return {
        answer: "No hay alertas críticas abiertas en la flota de demostración.",
        sources: ["GetVehiclesWithCriticalAlerts"],
      };
    }

    const lines = critical.slice(0, 8).map((a) => {
      const name =
        vehicles.find((v) => v.deviceId === a.deviceId)?.vehicleName ?? a.deviceId;
      return `- ${name}: ${a.message}`;
    });
    return {
      answer: `Alertas críticas (${critical.length}):\n${lines.join("\n")}`,
      sources: ["GetVehiclesWithCriticalAlerts"],
    };
  }

  if (intent.kind === "stopped") {
    const stopped = vehicles.filter(
      (v) => (v.lastSpeedKmh ?? 0) <= 1 || v.status === "offline",
    );
    if (stopped.length === 0) {
      return {
        answer: "No hay vehículos detenidos en la flota de demostración.",
        sources: ["GetStoppedVehicles"],
      };
    }

    return {
      answer: `Vehículos detenidos o sin movimiento (${stopped.length}):\n${stopped
        .slice(0, 10)
        .map(formatDemoVehicleLine)
        .join("\n")}`,
      sources: ["GetStoppedVehicles"],
    };
  }

  if (intent.kind === "speed") {
    const above = vehicles.filter((v) => (v.lastSpeedKmh ?? 0) > intent.speedKmh);
    if (above.length === 0) {
      return {
        answer: `Ningún vehículo supera ${intent.speedKmh.toFixed(0)} km/h en la flota de demostración.`,
        sources: ["GetVehiclesAboveSpeed"],
      };
    }

    return {
      answer: `Vehículos por encima de ${intent.speedKmh.toFixed(0)} km/h (${above.length}):\n${above
        .slice(0, 10)
        .map(formatDemoVehicleLine)
        .join("\n")}`,
      sources: ["GetVehiclesAboveSpeed"],
    };
  }

  if (intent.kind === "unsupported") {
    return {
      answer:
        "Solo respondo consultas operativas de flota: resumen, alertas críticas, detenidos o velocidad.",
      sources: [],
    };
  }

  const online = vehicles.filter((v) => v.status === "online").length;
  const criticalCount = alerts.filter((a) => /critical|crític/i.test(a.severity)).length;
  return {
    answer: `Hay ${criticalCount} alerta(s) crítica(s) y ${alerts.length} alerta(s) abiertas. ${online} vehículos en línea de ${vehicles.length} en la flota.`,
    sources: ["GetFleetOverview", "GetVehiclesWithCriticalAlerts"],
  };
}
