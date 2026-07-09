import { useCallback, useEffect, useRef, useState } from "react";
import { enqueueEvent, countPendingEvents } from "@/db/offline-queue";
import { getCurrentReading, watchReading } from "@/services/location-provider";
import { syncPendingQueue } from "@/services/sync-service";
import { generateEventId } from "@/utils/id";
import { useNetworkStatus } from "@/hooks/use-network-status";
import type { LocationReading, SyncResult } from "@/types/telemetry";

type DriverTelemetryState = {
  tracking: boolean;
  pendingCount: number;
  lastReading: LocationReading | null;
  lastCapturedAt: string | null;
  lastSync: SyncResult | null;
  error: string | null;
};

export function useDriverTelemetry(vehicleId: string, driverId: string) {
  const { isOnline, status: networkStatus } = useNetworkStatus();
  const stopWatchRef = useRef<(() => void) | null>(null);

  const [state, setState] = useState<DriverTelemetryState>({
    tracking: false,
    pendingCount: 0,
    lastReading: null,
    lastCapturedAt: null,
    lastSync: null,
    error: null,
  });

  const refreshPendingCount = useCallback(async () => {
    const pendingCount = await countPendingEvents();
    setState((prev) => ({ ...prev, pendingCount }));
  }, []);

  const captureEvent = useCallback(
    async (reading: LocationReading) => {
      const event = {
        eventId: await generateEventId(),
        vehicleId,
        driverId: driverId || null,
        timestamp: new Date().toISOString(),
        latitude: reading.latitude,
        longitude: reading.longitude,
        speedKmh: reading.speedKmh,
        fuelLevelPercent: Math.round(40 + Math.random() * 50),
        batteryPercent: Math.round(70 + Math.random() * 25),
      };

      await enqueueEvent(event);
      await refreshPendingCount();

      setState((prev) => ({
        ...prev,
        lastReading: reading,
        lastCapturedAt: event.timestamp,
        error: null,
      }));
    },
    [vehicleId, driverId, refreshPendingCount],
  );

  const syncNow = useCallback(async () => {
    try {
      const result = await syncPendingQueue(isOnline);
      await refreshPendingCount();
      setState((prev) => ({ ...prev, lastSync: result, error: null }));
      return result;
    } catch (error) {
      const message = error instanceof Error ? error.message : "Error al sincronizar";
      setState((prev) => ({ ...prev, error: message }));
      throw error;
    }
  }, [isOnline, refreshPendingCount]);

  const stopTracking = useCallback(() => {
    stopWatchRef.current?.();
    stopWatchRef.current = null;
    setState((prev) => ({ ...prev, tracking: false }));
  }, []);

  const startTracking = useCallback(async () => {
    if (stopWatchRef.current) return;

    const stop = await watchReading(async (reading) => {
      await captureEvent(reading);
      if (isOnline) {
        await syncNow();
      }
    }, 8000);

    stopWatchRef.current = stop;
    setState((prev) => ({ ...prev, tracking: true, error: null }));
  }, [captureEvent, isOnline, syncNow]);

  const captureOnce = useCallback(async () => {
    const reading = await getCurrentReading();
    await captureEvent(reading);
    if (isOnline) {
      await syncNow();
    }
  }, [captureEvent, isOnline, syncNow]);

  useEffect(() => {
    refreshPendingCount();
  }, [refreshPendingCount]);

  useEffect(() => {
    if (isOnline) {
      syncNow().catch(() => undefined);
    }
  }, [isOnline, syncNow]);

  useEffect(() => {
    return () => {
      stopWatchRef.current?.();
    };
  }, []);

  return {
    ...state,
    networkStatus,
    isOnline,
    startTracking,
    stopTracking,
    captureOnce,
    syncNow,
    refreshPendingCount,
  };
}
