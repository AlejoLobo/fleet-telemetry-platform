// Prueba de carga k6: ingesta de telemetría (5% inválidos, 10% duplicados reales, 85% nuevos).
import http from "k6/http";
import { check, sleep } from "k6";
import { Counter, Rate, Trend } from "k6/metrics";
import { randomIntBetween, uuidv4 } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const API_URL = __ENV.API_URL || "http://localhost:5000";
const AUTH_TOKEN = __ENV.AUTH_TOKEN || "";
const VEHICLE_COUNT = Number(__ENV.VEHICLES || 300);
const DUPLICATE_POOL_SIZE = 50;

const BOGOTA_ZONES = [
  { lat: 4.648, lng: -74.063, spread: 0.018 },
  { lat: 4.711, lng: -74.032, spread: 0.015 },
  { lat: 4.737, lng: -74.082, spread: 0.02 },
  { lat: 4.628, lng: -74.152, spread: 0.022 },
  { lat: 4.598, lng: -74.075, spread: 0.012 },
  { lat: 4.702, lng: -74.108, spread: 0.016 },
  { lat: 4.628, lng: -74.09, spread: 0.014 },
  { lat: 4.669, lng: -74.145, spread: 0.015 },
  { lat: 4.568, lng: -74.085, spread: 0.018 },
  { lat: 4.612, lng: -74.195, spread: 0.02 },
];

const acceptedEvents = new Counter("telemetry_accepted");
const duplicateEvents = new Counter("telemetry_duplicate_sent");
const intentionalInvalid = new Counter("telemetry_intentional_invalid");
const unexpectedFailureRate = new Rate("telemetry_unexpected_failure_rate");
const validAcceptedRate = new Rate("telemetry_valid_accepted_rate");
const invalidRejectedRate = new Rate("telemetry_invalid_rejected_rate");
const validRequestDuration = new Trend("telemetry_valid_request_duration", true);

function headers(deviceId) {
  const h = { "Content-Type": "application/json" };
  if (AUTH_TOKEN) h.Authorization = `Bearer ${AUTH_TOKEN}`;
  if (deviceId) h["X-Device-Id"] = deviceId;
  return h;
}

/** UUID estable por índice de vehículo (misma partición en múltiples corridas). */
function deviceIdForIndex(index) {
  const hex = String(index).padStart(12, "0");
  return `aaaaaaaa-bbbb-4ccc-8ddd-${hex}`;
}

function buildDuplicateSeedPayload() {
  const eventId = uuidv4();
  const index = randomIntBetween(1, VEHICLE_COUNT);
  const deviceId = deviceIdForIndex(index);
  const zone = BOGOTA_ZONES[index % BOGOTA_ZONES.length];
  const timestamp = new Date(Date.now() - 60_000).toISOString();
  return {
    deviceId,
    body: JSON.stringify({
      eventId,
      deviceId,
      driverId: `DRV-${String(index).padStart(3, "0")}`,
      timestamp,
      latitude: zone.lat,
      longitude: zone.lng,
      speedKmh: 42,
      fuelLevelPercent: 55,
      batteryPercent: 70,
    }),
  };
}

export function setup() {
  // Registra un subconjunto de dispositivos estables usados por la carga.
  for (let i = 1; i <= Math.min(VEHICLE_COUNT, 50); i++) {
    const deviceId = deviceIdForIndex(i);
    const reg = http.post(
      `${API_URL}/api/devices/register`,
      JSON.stringify({ deviceId }),
      { headers: headers(deviceId), responseCallback: http.expectedStatuses(200) },
    );
    if (reg.status !== 200) {
      throw new Error(`Setup: registro device ${deviceId} falló con HTTP ${reg.status}`);
    }
  }

  const duplicatePayloadPool = Array.from({ length: DUPLICATE_POOL_SIZE }, () => buildDuplicateSeedPayload());

  for (let i = 0; i < duplicatePayloadPool.length; i++) {
    const item = duplicatePayloadPool[i];
    const res = http.post(`${API_URL}/api/telemetry`, item.body, {
      headers: headers(item.deviceId),
      responseCallback: http.expectedStatuses(202),
    });

    const ok = check(res, {
      [`semilla duplicado ${i + 1} aceptada (202)`]: (r) => r.status === 202,
    });

    if (!ok) {
      throw new Error(`Setup: falló la siembra del duplicado ${i + 1} con HTTP ${res.status}`);
    }
  }

  return { duplicatePayloadPool };
}

export const options = {
  scenarios: {
    ingest: {
      executor: "constant-vus",
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || "30s",
    },
  },
  thresholds: {
    telemetry_unexpected_failure_rate: ["rate<0.01"],
    http_req_duration: ["p(95)<800", "p(99)<1500"],
    telemetry_valid_request_duration: ["p(95)<800", "p(99)<1500"],
    telemetry_valid_accepted_rate: ["rate>0.95"],
    telemetry_invalid_rejected_rate: ["rate>0.95"],
  },
};

function deviceIndex() {
  return randomIntBetween(1, VEHICLE_COUNT);
}

function randomPointInZone(zone) {
  const angle = Math.random() * Math.PI * 2;
  const radius = Math.random() * zone.spread;
  return {
    lat: Math.round((zone.lat + Math.cos(angle) * radius) * 1e5) / 1e5,
    lng: Math.round((zone.lng + Math.sin(angle) * radius) * 1e5) / 1e5,
  };
}

function locationForIndex(index) {
  const zone = BOGOTA_ZONES[index % BOGOTA_ZONES.length];
  return randomPointInZone(zone);
}

function randomTimestamp() {
  const online = Math.random() < 0.62;
  if (online) {
    const secondsAgo = randomIntBetween(0, 240);
    return new Date(Date.now() - secondsAgo * 1000).toISOString();
  }
  const minutesAgo = randomIntBetween(8, 55);
  return new Date(Date.now() - minutesAgo * 60 * 1000).toISOString();
}

function buildValidPayload(eventId, index) {
  const deviceId = deviceIdForIndex(index);
  const { lat, lng } = locationForIndex(index);
  const online = Math.random() < 0.62;
  return {
    deviceId,
    body: JSON.stringify({
      eventId,
      deviceId,
      driverId: `DRV-${String(index).padStart(3, "0")}`,
      timestamp: randomTimestamp(),
      latitude: lat,
      longitude: lng,
      speedKmh: online ? randomIntBetween(15, 130) : randomIntBetween(0, 12),
      fuelLevelPercent: randomIntBetween(5, 95),
      batteryPercent: randomIntBetween(25, 100),
    }),
  };
}

function buildInvalidPayload() {
  return JSON.stringify({
    eventId: uuidv4(),
    deviceId: "00000000-0000-0000-0000-000000000000",
    driverId: "DRV-LOAD",
    timestamp: "fecha-invalida",
    latitude: 999,
    longitude: -74.08,
    speedKmh: -10,
    fuelLevelPercent: 50,
    batteryPercent: 50,
  });
}

export default function (data) {
  const pool = data.duplicatePayloadPool;

  // Una sola variable aleatoria: [0,0.05) inválido, [0.05,0.15) duplicado, [0.15,1) nuevo.
  const roll = Math.random();

  if (roll < 0.05) {
    const res = http.post(`${API_URL}/api/telemetry`, buildInvalidPayload(), {
      headers: headers(),
      responseCallback: http.expectedStatuses(400),
    });
    intentionalInvalid.add(1);
    const ok = res.status === 400;
    invalidRejectedRate.add(ok);
    unexpectedFailureRate.add(!ok);
    check(res, { "error intencional (400)": (r) => r.status === 400 });
    sleep(0.15);
    return;
  }

  if (roll < 0.15) {
    const item = pool[randomIntBetween(0, pool.length - 1)];
    duplicateEvents.add(1);
    const start = Date.now();
    const res = http.post(`${API_URL}/api/telemetry`, item.body, {
      headers: headers(item.deviceId),
      responseCallback: http.expectedStatuses(202),
    });
    const duration = Date.now() - start;
    const ok = res.status === 202;
    validAcceptedRate.add(ok);
    unexpectedFailureRate.add(!ok);
    if (ok) {
      acceptedEvents.add(1);
      validRequestDuration.add(duration);
    }
    check(res, { "duplicado aceptado (202)": (r) => r.status === 202 });
    sleep(0.15);
    return;
  }

  const index = deviceIndex();
  const item = buildValidPayload(uuidv4(), index);
  const start = Date.now();
  const res = http.post(`${API_URL}/api/telemetry`, item.body, {
    headers: headers(item.deviceId),
    responseCallback: http.expectedStatuses(202),
  });
  const duration = Date.now() - start;
  const ok = res.status === 202;
  validAcceptedRate.add(ok);
  unexpectedFailureRate.add(!ok);
  if (ok) {
    acceptedEvents.add(1);
    validRequestDuration.add(duration);
  }
  check(res, { "aceptado (202)": (r) => r.status === 202 });
  sleep(0.15);
}
