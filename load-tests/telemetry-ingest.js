import http from "k6/http";
import { check, sleep } from "k6";
import { Counter, Rate, Trend } from "k6/metrics";
import { randomIntBetween, uuidv4 } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const API_URL = __ENV.API_URL || "http://localhost:5000";
const AUTH_TOKEN = __ENV.AUTH_TOKEN || "";
const VEHICLE_COUNT = Number(__ENV.VEHICLES || 300);
const DUPLICATE_RATE = Number(__ENV.DUPLICATE_RATE || 0.1);
const ERROR_RATE = Number(__ENV.ERROR_RATE || 0.05);

// Métricas separadas para caos controlado vs errores reales inesperados
const acceptedEvents = new Counter("telemetry_accepted");
const duplicateEvents = new Counter("telemetry_duplicate_sent");
const intentionalInvalid = new Counter("telemetry_intentional_invalid");
const unexpectedFailures = new Counter("telemetry_unexpected_failure");
const intentionalErrorRate = new Rate("telemetry_intentional_error_rate");
const validRequestDuration = new Trend("telemetry_valid_request_duration", true);

// Pool de eventIds para reutilizar en duplicados (10% por defecto)
const duplicateEventIds = Array.from({ length: 50 }, () => uuidv4());

export const options = {
  scenarios: {
    ingest: {
      executor: "constant-vus",
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || "30s",
    },
  },
  thresholds: {
  // Solo fallos no intencionales deben contar para el umbral global
    http_req_failed: ["rate<0.05"],
    http_req_duration: ["p(95)<800"],
    telemetry_unexpected_failure: ["count<50"],
    telemetry_valid_request_duration: ["p(95)<800"],
  },
};

function headers() {
  const h = { "Content-Type": "application/json" };
  if (AUTH_TOKEN) h.Authorization = `Bearer ${AUTH_TOKEN}`;
  return h;
}

function vehicleId() {
  return `VH-${String(randomIntBetween(1, VEHICLE_COUNT)).padStart(3, "0")}`;
}

function buildValidPayload(eventId, vehicle) {
  return JSON.stringify({
    eventId,
    vehicleId: vehicle,
    driverId: "DRV-LOAD",
    timestamp: new Date().toISOString(),
    latitude: 4.65 + Math.random() * 0.05,
    longitude: -74.08 - Math.random() * 0.05,
    speedKmh: randomIntBetween(20, 130),
    fuelLevelPercent: randomIntBetween(5, 95),
    batteryPercent: randomIntBetween(40, 100),
  });
}

function buildInvalidPayload() {
  return JSON.stringify({
    eventId: uuidv4(),
    vehicleId: "",
    driverId: "DRV-LOAD",
    timestamp: "fecha-invalida",
    latitude: 999,
    longitude: -74.08,
    speedKmh: -10,
    fuelLevelPercent: 50,
    batteryPercent: 50,
  });
}

export default function () {
  const roll = Math.random();
  const vehicle = vehicleId();

  if (roll < ERROR_RATE) {
    const res = http.post(`${API_URL}/api/telemetry`, buildInvalidPayload(), { headers: headers() });
    intentionalInvalid.add(1);
    intentionalErrorRate.add(1);
    check(res, {
      "error intencional (400)": (r) => r.status === 400,
    });
    if (res.status !== 400) {
      unexpectedFailures.add(1);
    }
    sleep(0.15);
    return;
  }

  const isDuplicate = roll < ERROR_RATE + DUPLICATE_RATE;
  const eventId = isDuplicate
    ? duplicateEventIds[randomIntBetween(0, duplicateEventIds.length - 1)]
    : uuidv4();

  if (isDuplicate) {
    duplicateEvents.add(1);
  }

  const payload = buildValidPayload(eventId, vehicle);
  const start = Date.now();
  const res = http.post(`${API_URL}/api/telemetry`, payload, { headers: headers() });
  const duration = Date.now() - start;

  intentionalErrorRate.add(0);

  const ok = check(res, {
    "aceptado (202)": (r) => r.status === 202,
  });

  if (ok) {
    acceptedEvents.add(1);
    validRequestDuration.add(duration);
  } else {
    unexpectedFailures.add(1);
  }

  sleep(0.15);
}
