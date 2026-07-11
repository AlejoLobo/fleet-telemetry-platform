import * as Location from "expo-location";
import { isSimulatedLocationAllowed } from "@/config/env";
import type { LocationReading } from "@/types/telemetry";

export class LocationCaptureError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "LocationCaptureError";
  }
}

function simulate(): LocationReading {
  if (!isSimulatedLocationAllowed()) {
    throw new LocationCaptureError(
      "GPS no disponible y la simulación está deshabilitada. Configure EXPO_PUBLIC_ALLOW_SIMULATED_LOCATION=true solo en desarrollo.",
    );
  }

  const jitter = () => (Math.random() - 0.5) * 0.01;
  return {
    latitude: 4.711 + jitter(),
    longitude: -74.0721 + jitter(),
    speedKmh: Math.round(20 + Math.random() * 40),
    source: "simulated",
  };
}

export async function getCurrentReading(): Promise<LocationReading> {
  try {
    const { status } = await Location.requestForegroundPermissionsAsync();
    if (status !== "granted") {
      if (!isSimulatedLocationAllowed()) {
        throw new LocationCaptureError("Permiso de ubicación denegado.");
      }
      return simulate();
    }

    const position = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
    return {
      latitude: position.coords.latitude,
      longitude: position.coords.longitude,
      speedKmh: Math.max(0, Math.round((position.coords.speed ?? 0) * 3.6)),
      source: "gps",
    };
  } catch (error) {
    if (error instanceof LocationCaptureError) throw error;
    if (!isSimulatedLocationAllowed()) {
      throw new LocationCaptureError(
        error instanceof Error ? error.message : "No se pudo capturar ubicación GPS",
      );
    }
    return simulate();
  }
}

export async function runCaptureLoop(
  onReading: (reading: LocationReading) => void | Promise<void>,
  intervalMs: number,
  shouldContinue: () => boolean,
): Promise<void> {
  while (shouldContinue()) {
    const started = Date.now();
    await onReading(await getCurrentReading());
    await new Promise((resolve) => setTimeout(resolve, Math.max(0, intervalMs - (Date.now() - started))));
  }
}
