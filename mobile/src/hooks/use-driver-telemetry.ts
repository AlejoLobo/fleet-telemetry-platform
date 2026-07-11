import { useCallback, useEffect, useRef, useState } from "react";
import { enqueueEvent, countPendingEvents } from "@/db/offline-queue";
import { getCurrentReading, runCaptureLoop } from "@/services/location-provider";
import { syncPendingQueue } from "@/services/offline-sync-coordinator";
import { generateEventId } from "@/utils/id";
import { useNetworkStatus } from "@/hooks/use-network-status";
import type { LocationReading, SyncResult } from "@/types/telemetry";

export function useDriverTelemetry(vehicleId: string, driverId: string) {
  const { isOnline, status: networkStatus } = useNetworkStatus();
  const trackingRef = useRef(false);
  const [state, setState] = useState({ tracking: false, pendingCount: 0, lastReading: null as LocationReading | null, lastCapturedAt: null as string | null, lastSync: null as SyncResult | null, error: null as string | null });

  const refreshPendingCount = useCallback(async () => {
    const pendingCount = await countPendingEvents();
    setState((p) => ({ ...p, pendingCount }));
  }, []);

  const captureEvent = useCallback(async (reading: LocationReading) => {
    const event = { eventId: await generateEventId(), vehicleId, driverId: driverId || null, timestamp: new Date().toISOString(), latitude: reading.latitude, longitude: reading.longitude, speedKmh: reading.speedKmh, fuelLevelPercent: Math.round(40 + Math.random() * 50), batteryPercent: Math.round(70 + Math.random() * 25), locationSource: reading.source };
    await enqueueEvent(event, reading.source);
    await refreshPendingCount();
    setState((p) => ({ ...p, lastReading: reading, lastCapturedAt: event.timestamp, error: null }));
  }, [vehicleId, driverId, refreshPendingCount]);

  const syncNow = useCallback(async () => {
    const result = await syncPendingQueue(isOnline);
    await refreshPendingCount();
    setState((p) => ({ ...p, lastSync: result, error: null }));
    return result;
  }, [isOnline, refreshPendingCount]);

  const stopTracking = useCallback(() => { trackingRef.current = false; setState((p) => ({ ...p, tracking: false })); }, []);
  const startTracking = useCallback(async () => {
    if (trackingRef.current) return;
    trackingRef.current = true;
    setState((p) => ({ ...p, tracking: true, error: null }));
    runCaptureLoop(async (reading) => { await captureEvent(reading); if (isOnline) await syncNow(); }, 8000, () => trackingRef.current)
      .catch((e) => { trackingRef.current = false; setState((p) => ({ ...p, tracking: false, error: e instanceof Error ? e.message : "Tracking error" })); });
  }, [captureEvent, isOnline, syncNow]);

  const captureOnce = useCallback(async () => { await captureEvent(await getCurrentReading()); if (isOnline) await syncNow(); }, [captureEvent, isOnline, syncNow]);

  useEffect(() => { refreshPendingCount(); }, [refreshPendingCount]);
  useEffect(() => { if (isOnline) syncNow().catch(() => undefined); }, [isOnline, syncNow]);
  useEffect(() => () => { trackingRef.current = false; }, []);

  return { ...state, networkStatus, isOnline, startTracking, stopTracking, captureOnce, syncNow, refreshPendingCount };
};
