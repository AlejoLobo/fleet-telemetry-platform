import { useCallback, useEffect, useRef, useState } from "react";
import {
  TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS,
  TELEMETRY_SYNC_INTERVAL_MILLISECONDS,
} from "@/config/telemetry-capture-rate";
import { enqueueEvent, countPendingEvents } from "@/db/offline-queue";
import { getCurrentReading, runCaptureLoop } from "@/services/location-provider";
import { syncPendingQueue } from "@/services/offline-sync-coordinator";
import { runSyncResumeEffect } from "@/services/sync-resume-policy";
import { generateEventId } from "@/utils/id";
import { useNetworkStatus } from "@/hooks/use-network-status";
import type { LocationReading, SyncResult } from "@/types/telemetry";

export function useDriverTelemetry(
  deviceId: string,
  driverId: string,
  canSync: boolean,
) {
  const { isOnline, status: networkStatus } = useNetworkStatus();
  const trackingRef = useRef(false);
  const previousReadyToSyncRef = useRef(false);
  const syncTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const syncPromiseRef = useRef<Promise<SyncResult> | null>(null);
  const syncRequestedRef = useRef(false);
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

  /** Single-flight: una sola sync activa; reentra si hubo solicitudes durante la ejecución. */
  const requestSync = useCallback(async (): Promise<SyncResult | null> => {
    if (syncPromiseRef.current) {
      syncRequestedRef.current = true;
      return syncPromiseRef.current;
    }

    const run = async (): Promise<SyncResult> => {
      let lastResult: SyncResult = {
        synced: 0,
        failed: 0,
        retried: 0,
        permanentFailures: 0,
        remaining: 0,
        status: "completed",
      };

      do {
        syncRequestedRef.current = false;
        try {
          lastResult = await syncPendingQueue(
            isOnlineRef.current,
            deviceIdRef.current,
          );
          await refreshPendingCount();
          setState((p) => ({ ...p, lastSync: lastResult, error: null }));
        } catch (error) {
          const message =
            error instanceof Error ? error.message : "Error de sincronización";
          setState((p) => ({ ...p, error: message }));
          await refreshPendingCount();
        }
      } while (syncRequestedRef.current);

      return lastResult;
    };

    const promise = run().finally(() => {
      syncPromiseRef.current = null;
      // Solicitud entre el fin del ciclo y el clear: otra pasada sin solapar.
      if (syncRequestedRef.current) {
        void requestSync();
      }
    });
    syncPromiseRef.current = promise;
    return promise;
  }, [refreshPendingCount]);

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

    if (isOnlineRef.current && canSyncRef.current) {
      await requestSync().catch((error) => {
        setState((previous) => ({
          ...previous,
          error: error instanceof Error ? error.message : "Error de sincronización",
        }));
      });
    }
  }, [driverId, refreshPendingCount, requestSync]);

  const syncNow = useCallback(async () => {
    const result = await requestSync();
    return result ?? {
      synced: 0,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: await countPendingEvents(),
      status: "completed" as const,
    };
  }, [requestSync]);

  const startSyncTimer = useCallback((): void => {
    clearSyncTimer();

    if (!trackingRef.current || !canSyncRef.current) {
      return;
    }

    syncTimerRef.current = setInterval(() => {
      if (!trackingRef.current) return;
      if (!isOnlineRef.current || !canSyncRef.current) return;
      if (syncPromiseRef.current) {
        syncRequestedRef.current = true;
        return;
      }

      void requestSync().catch((error) => {
        setState((previous) => ({
          ...previous,
          error: error instanceof Error
            ? error.message
            : "Error de sincronización",
        }));
      });
    }, TELEMETRY_SYNC_INTERVAL_MILLISECONDS);
  }, [clearSyncTimer, requestSync]);

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
      TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS,
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
  }, [captureEvent, clearSyncTimer, startSyncTimer]);

  const captureOnce = useCallback(async () => {
    await captureEvent(await getCurrentReading());
  }, [captureEvent]);

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

export {
  TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS,
  TELEMETRY_SYNC_INTERVAL_MILLISECONDS,
};
