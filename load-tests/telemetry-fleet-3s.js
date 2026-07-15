// Carga de flota: N dispositivos publicando 1 evento cada 3 s (VU = dispositivo).
// Ejemplo: k6 run -e DEVICES=100 -e DURATION=60s load-tests/telemetry-fleet-3s.js
import http from "k6/http";
import { check, sleep } from "k6";
import { Counter, Rate, Trend } from "k6/metrics";
import { uuidv4 } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const API_URL = __ENV.API_URL || "http://localhost:5000";
const AUTH_TOKEN = __ENV.AUTH_TOKEN || "";
const DEVICE_COUNT = Number(__ENV.DEVICES || 100);
const INTERVAL_SECONDS = Number(__ENV.INTERVAL_SECONDS || 3);
const DURATION = __ENV.DURATION || "60s";

const accepted = new Counter("fleet_telemetry_accepted");
const failureRate = new Rate("fleet_telemetry_failure_rate");
const latency = new Trend("fleet_telemetry_latency", true);

function headers() {
  const h = { "Content-Type": "application/json" };
  if (AUTH_TOKEN) h.Authorization = `Bearer ${AUTH_TOKEN}`;
  return h;
}

export const options = {
  scenarios: {
    fleet_every_interval: {
      executor: "constant-vus",
      vus: DEVICE_COUNT,
      duration: DURATION,
    },
  },
  thresholds: {
    fleet_telemetry_failure_rate: ["rate<0.01"],
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<1000", "p(99)<2000"],
    fleet_telemetry_latency: ["p(95)<1000", "p(99)<2000"],
  },
};

export default function () {
  const vu = __VU;
  const vehicleId = `VH-${String(vu).padStart(3, "0")}`;
  const payload = JSON.stringify({
    eventId: uuidv4(),
    vehicleId,
    driverId: `DRV-${String(vu).padStart(3, "0")}`,
    timestamp: new Date().toISOString(),
    latitude: 4.65 + (vu % 50) * 0.001,
    longitude: -74.08 - (vu % 50) * 0.001,
    speedKmh: 30 + (vu % 40),
    fuelLevelPercent: 50,
    batteryPercent: 80,
  });

  const started = Date.now();
  const res = http.post(`${API_URL}/api/telemetry`, payload, {
    headers: headers(),
    responseCallback: http.expectedStatuses(202),
  });
  const ok = res.status === 202;
  failureRate.add(!ok);
  if (ok) {
    accepted.add(1);
    latency.add(Date.now() - started);
  }
  check(res, { "aceptado 202": (r) => r.status === 202 });

  sleep(INTERVAL_SECONDS);
}
