import * as Location from "expo-location";
import type { LocationReading } from "@/types/telemetry";

const BOGOTA_LAT = 4.711;
const BOGOTA_LNG = -74.0721;

function simulateReading(): LocationReading {
  const jitter = () => (Math.random() - 0.5) * 0.01;
  return {
    latitude: BOGOTA_LAT + jitter(),
    longitude: BOGOTA_LNG + jitter(),
    speedKmh: Math.round(20 + Math.random() * 40),
    source: "simulated",
  };
}

export async function getCurrentReading(): Promise<LocationReading> {
  try {
    const { status } = await Location.requestForegroundPermissionsAsync();
    if (status !== "granted") {
      return simulateReading();
    }

    const position = await Location.getCurrentPositionAsync({
      accuracy: Location.Accuracy.Balanced,
    });

    const speedMs = position.coords.speed ?? 0;
    const speedKmh = Math.max(0, Math.round(speedMs * 3.6));

    return {
      latitude: position.coords.latitude,
      longitude: position.coords.longitude,
      speedKmh,
      source: "gps",
    };
  } catch {
    return simulateReading();
  }
}

export async function watchReading(
  onReading: (reading: LocationReading) => void,
  intervalMs = 5000,
): Promise<() => void> {
  let active = true;

  const tick = async () => {
    if (!active) return;
    const reading = await getCurrentReading();
    onReading(reading);
  };

  await tick();
  const timer = setInterval(tick, intervalMs);

  return () => {
    active = false;
    clearInterval(timer);
  };
}
