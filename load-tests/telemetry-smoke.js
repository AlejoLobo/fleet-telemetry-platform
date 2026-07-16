// Smoke k6: pocas iteraciones para validar ingesta en CI.
import http from "k6/http";
import { check } from "k6";
import { uuidv4 } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const API_URL = __ENV.API_URL || "http://localhost:5000";

export const options = {
  vus: 1,
  iterations: 5,
  thresholds: {
    http_req_failed: ["rate<0.2"],
    checks: ["rate>0.8"],
  },
};

export function setup() {
  const deviceId = uuidv4();
  const registerRes = http.post(
    `${API_URL}/api/devices/register`,
    JSON.stringify({ deviceId }),
    {
      headers: {
        "Content-Type": "application/json",
        "X-Device-Id": deviceId,
      },
    },
  );

  if (registerRes.status !== 200) {
    throw new Error(`Setup: registro de dispositivo falló con HTTP ${registerRes.status}`);
  }

  return { deviceId };
}

export default function (data) {
  const deviceId = data.deviceId;
  const payload = JSON.stringify({
    eventId: uuidv4(),
    deviceId,
    driverId: "DRV-SMOKE",
    timestamp: new Date().toISOString(),
    latitude: 4.711,
    longitude: -74.0721,
    speedKmh: 35,
    fuelLevelPercent: 70,
    batteryPercent: 90,
    locationSource: "gps",
  });

  const res = http.post(`${API_URL}/api/telemetry`, payload, {
    headers: {
      "Content-Type": "application/json",
      "X-Device-Id": deviceId,
    },
  });

  check(res, {
    "ingesta aceptada (202)": (r) => r.status === 202,
  });
}
