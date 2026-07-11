import * as Location from "expo-location";
import { isSimulatedLocationAllowed } from "@/config/env";
import type { LocationReading } from "@/types/telemetry";

class SimulatedLocationDisabledError extends Error {
  constructor() { super("Simulated location disabled. Set EXPO_PUBLIC_ALLOW_SIMULATED_LOCATION=true."); this.name = "SimulatedLocationDisabledError"; }
}

function simulate(): LocationReading {
  if (!isSimulatedLocationAllowed()) throw new SimulatedLocationDisabledError();
  const jitter = () => (Math.random() - 0.5) * 0.01;
  return { latitude: 4.711 + jitter(), longitude: -74.0721 + jitter(), speedKmh: Math.round(20 + Math.random() * 40), source: "simulated" };
}

export async function getCurrentReading(): Promise<LocationReading> {
  try {
    const { status } = await Location.requestForegroundPermissionsAsync();
    if (status !== "granted") return simulate();
    const position = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
    return { latitude: position.coords.latitude, longitude: position.coords.longitude, speedKmh: Math.max(0, Math.round((position.coords.speed ?? 0) * 3.6)), source: "gps" };
  } catch (error) {
    if (error instanceof SimulatedLocationDisabledError) throw error;
    return simulate();
  }
}

export async function runCaptureLoop(onReading: (r: LocationReading) => void | Promise<void>, intervalMs: number, shouldContinue: () => boolean): Promise<void> {
  while (shouldContinue()) {
    const started = Date.now();
    try { await onReading(await getCurrentReading()); } catch (e) { if (e instanceof SimulatedLocationDisabledError) throw e; }
    await new Promise((r) => setTimeout(r, Math.max(0, intervalMs - (Date.now() - started))));
  }
}
