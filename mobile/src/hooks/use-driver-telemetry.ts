import { useCallback, useEffect, useRef, useState } from "react";
import {
  DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS,
  type TelemetryCaptureIntervalSeconds,
} from "@/config/telemetry-capture-rate";
import { enqueueEvent, countPendingEvents } from "@/db/offline-queue";
import { getCurrentReading, runCaptureLoop } from "@/services/location-provider";
import { syncPendingQueue } from "@/services/offline-sync-coordinator";
import { runSyncResumeEffect } from "@/services/sync-resume-policy";
import { generateEventId } from "@/utils/id";
import { useNetworkStatus } from "@/hooks/use-network-status";
import type { LocationReading, SyncResult } from "@/types/telemetry";

const SYNC_INTERVAL_MILLISECONDS = 10_000;

export function useDriverTelemetry(
  deviceId: string,
  driverId: string,
  canSync: boolean,
  captureIntervalSeconds: TelemetryCaptureIntervalSeconds = DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS,
) {
  const { isOnline, status: networkStatus } = useNetworkStatus();
  const trackingRef = useRef(false);
  const previousReadyToSyncRef = useRef(false);
  const syncTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const isOnlineRef = useRef(isOnline);
  const canSyncRef = useRef(canSync);
  const deviceIdRef = useRef(deviceId);
  const [state, setState] = useState({
    tracking: false,
    pendingCount: 0,
    lastReading: null as LocationReading | null,
    lastCapturedAt: null as string | null,
    lastSync: null as SyncResult | null,
    error: null as string | null,
  });

  isOnlineRef.current = isOnline;
  canSyncRef.current = canSync;
  deviceIdRef.current = deviceId;

  const clearSyncTimer = useCallback((): void => {
    if (syncTimerRef.current !== null) {
      clearInterval(syncTimerRef.current);
      syncTimerRef.current = null;
    }
  }, []);

  const refreshPendingCount = useCallback(async () => {
    const pendingCount = await countPendingEvents();
    setState((p) => ({ ...p, pendingCount }));
  }, []);

  const captureEvent = useCallback(async (reading: LocationReading) => {
    const resolvedDeviceId = deviceIdRef.current.trim();
    if (!resolvedDeviceId) {
      setState((p) => ({ ...p, error: "deviceId no disponible" }));
      return;
    }

    const event = {
      eventId: await generateEventId(),
      deviceId: resolvedDeviceId,
      driverId: driverId || null,
      timestamp: new Date().toISOString(),
      latitude: reading.latitude,
      longitude: reading.longitude,
      speedKmh: reading.speedKmh,
      fuelLevelPercent: Math.round(40 + Math.random() * 50),
      batteryPercent: Math.round(70 + Math.random() * 25),
      locationSource: reading.source,
    };
    await enqueueEvent(event, reading.source);
    await refreshPendingCount();
    setState((p) => ({ ...p, lastReading: reading, lastCapturedAt: event.timestamp, error: null }));
  }, [driverId, refreshPendingCount]);

  const syncNow = useCallback(async () => {
    const result = await syncPendingQueue(isOnlineRef.current, deviceIdRef.current);
    await refreshPendingCount();
    setState((p) => ({ ...p, lastSync: result, error: null }));
    return result;
  }, [refreshPendingCount]);

  const startSyncTimer = useCallback((): void => {
    clearSyncTimer();

    if (!trackingRef.current || !canSyncRef.current) {
      return;
    }

    syncTimerRef.current = setInterval(() => {
      if (!trackingRef.current) return;
      if (!isOnlineRef.current || !canSyncRef.current) return;

      void syncNow().catch((error) => {
        setState((previous) => ({
          ...previous,
          error: error instanceof Error
            ? error.message
            : "Error de sincronización",
        }));
      });
    }, SYNC_INTERVAL_MILLISECONDS);
  }, [clearSyncTimer, syncNow]);

  const stopTracking = useCallback(async () => {
    clearSyncTimer();
    trackingRef.current = false;
    setState((p) => ({ ...p, tracking: false }));
    if (isOnlineRef.current && canSyncRef.current) {
      await syncNow();
    }
  }, [clearSyncTimer, syncNow]);

  const startTracking = useCallback(async () => {
    if (trackingRef.current) return;
    trackingRef.current = true;
    setState((p) => ({ ...p, tracking: true, error: null }));

    startSyncTimer();

    runCaptureLoop(
      async (reading) => {
        await captureEvent(reading);
      },
      captureIntervalSeconds * 1000,
      () => trackingRef.current,
    ).catch((e) => {
      clearSyncTimer();
      trackingRef.current = false;
      setState((p) => ({
        ...p,
        tracking: false,
        error: e instanceof Error ? e.message : "Tracking error",
      }));
    });
  }, [captureEvent, captureIntervalSeconds, clearSyncTimer, startSyncTimer]);

  const captureOnce = useCallback(async () => {
    await captureEvent(await getCurrentReading());
    if (isOnlineRef.current && canSyncRef.current) await syncNow();
  }, [captureEvent, syncNow]);

  useEffect(() => {
    refreshPendingCount();
  }, [refreshPendingCount]);

  useEffect(() => {
    const resume = runSyncResumeEffect(
      previousReadyToSyncRef.current,
      canSync,
      isOnline,
      () => syncNow(),
    );
    previousReadyToSyncRef.current = resume.nextPreviousReadyToSync;
  }, [isOnline, canSync, syncNow]);

  useEffect(() => {
    if (!canSync) {
      clearSyncTimer();
      return;
    }

    if (trackingRef.current) {
      startSyncTimer();
    }
  }, [canSync, clearSyncTimer, startSyncTimer]);

  useEffect(() => () => {
    trackingRef.current = false;
    clearSyncTimer();
  }, [clearSyncTimer]);

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

export { SYNC_INTERVAL_MILLISECONDS };
