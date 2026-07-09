import http from "k6/http";
import { check, sleep } from "k6";
import { randomIntBetween, uuidv4 } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const API_URL = __ENV.API_URL || "http://localhost:5000";
const AUTH_TOKEN = __ENV.AUTH_TOKEN || "";

export const options = {
  scenarios: {
    ingest: {
      executor: "constant-vus",
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || "30s",
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.05"],
    http_req_duration: ["p(95)<800"],
  },
};

function headers() {
  const h = { "Content-Type": "application/json" };
  if (AUTH_TOKEN) h.Authorization = `Bearer ${AUTH_TOKEN}`;
  return h;
}

export default function () {
  const vehicleId = `VH-${String(randomIntBetween(1, 20)).padStart(3, "0")}`;
  const payload = JSON.stringify({
    eventId: uuidv4(),
    vehicleId,
    driverId: "DRV-LOAD",
    timestamp: new Date().toISOString(),
    latitude: 4.65 + Math.random() * 0.05,
    longitude: -74.08 - Math.random() * 0.05,
    speedKmh: randomIntBetween(20, 90),
    fuelLevelPercent: randomIntBetween(30, 95),
    batteryPercent: randomIntBetween(40, 100),
  });

  const res = http.post(`${API_URL}/api/telemetry`, payload, { headers: headers() });
  check(res, {
    "aceptado (202)": (r) => r.status === 202,
  });
  sleep(0.2);
}
