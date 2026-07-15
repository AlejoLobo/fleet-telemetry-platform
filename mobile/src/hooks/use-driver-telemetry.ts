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

type CaptureOptions = {
  /** Si true, espera el resultado de sync (botón manual). Por defecto false. */
  awaitSync?: boolean;
};

export function useDriverTelemetry(
  deviceId: string,
  driverId: string,
  canSync: boolean,
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

  const applySyncError = useCallback((error: unknown) => {
    const message =
      error instanceof Error ? error.message : "Error de sincronización";
    setState((previous) => ({ ...previous, error: message }));
  }, []);

  /**
   * Solicita sync al coordinador (única autoridad de exclusión).
   * El hook solo actualiza estado visual; no duplica mutex.
   */
  const requestSync = useCallback(async (): Promise<SyncResult> => {
    try {
      const result = await syncPendingQueue(
        isOnlineRef.current,
        deviceIdRef.current,
      );
      await refreshPendingCount();

      let errorMessage: string | null = null;
      switch (result.status) {
        case "failed":
          errorMessage = "Error de sincronización";
          break;
        case "deferred":
          errorMessage = "Sincronización diferida";
          break;
        case "auth_required":
          errorMessage = "Login requerido";
          break;
        case "forbidden":
          errorMessage = "Permiso insuficiente";
          break;
        case "auth_status_error":
          errorMessage = "Error consultando auth status";
          break;
        case "configuration_error":
          errorMessage = "Configuración de dispositivo inválida";
          break;
        case "device_identity_conflict":
          errorMessage = "Conflicto de identidad de dispositivo";
          break;
        default:
          errorMessage = null;
      }

      setState((p) => ({
        ...p,
        lastSync: result,
        error: errorMessage,
      }));
      return result;
    } catch (error) {
      const remaining = await countPendingEvents();
      const failedResult: SyncResult = {
        synced: 0,
        failed: 0,
        retried: 0,
        permanentFailures: 0,
        remaining,
        status: "failed",
      };
      const message =
        error instanceof Error ? error.message : "Error de sincronización";
      setState((p) => ({
        ...p,
        lastSync: failedResult,
        pendingCount: remaining,
        error: message,
      }));
      return failedResult;
    }
  }, [refreshPendingCount]);

  const captureEvent = useCallback(async (
    reading: LocationReading,
    options: CaptureOptions = {},
  ) => {
    const awaitSync = options.awaitSync === true;
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
    setState((p) => ({
      ...p,
      lastReading: reading,
      lastCapturedAt: event.timestamp,
      error: null,
    }));

    if (isOnlineRef.current && canSyncRef.current) {
      const syncPromise = requestSync().catch((error) => {
        applySyncError(error);
        return null;
      });
      if (awaitSync) {
        await syncPromise;
      } else {
        void syncPromise;
      }
    }
  }, [applySyncError, driverId, refreshPendingCount, requestSync]);

  const syncNow = useCallback(async () => {
    return requestSync();
  }, [requestSync]);

  const startSyncTimer = useCallback((): void => {
    clearSyncTimer();

    if (!trackingRef.current || !canSyncRef.current) {
      return;
    }

    syncTimerRef.current = setInterval(() => {
      if (!trackingRef.current) return;
      if (!isOnlineRef.current || !canSyncRef.current) return;
      void requestSync().catch(applySyncError);
    }, TELEMETRY_SYNC_INTERVAL_MILLISECONDS);
  }, [applySyncError, clearSyncTimer, requestSync]);

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

    // Captura periódica: nunca espera a que termine la sync.
    runCaptureLoop(
      async (reading) => {
        await captureEvent(reading, { awaitSync: false });
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
    await captureEvent(await getCurrentReading(), { awaitSync: true });
  }, [captureEvent]);

  /** Captura para pruebas/loop: encola y dispara sync sin bloquear. */
  const captureAndQueue = useCallback(async () => {
    await captureEvent(await getCurrentReading(), { awaitSync: false });
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
    captureAndQueue,
    syncNow,
    refreshPendingCount,
  };
}

export {
  TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS,
  TELEMETRY_SYNC_INTERVAL_MILLISECONDS,
};
