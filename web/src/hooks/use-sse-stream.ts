/** Hook para conexión SSE autenticada vía fetch (FT-001 / FT-005). */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import {
  buildSseHeaders,
  clearSseCursorState,
  computeReconnectDelayMs,
  consumeSseFetchStream,
  isSseAuthError,
  readStoredLastEventId,
  readStoredResyncMarker,
  writeStoredLastEventId,
  writeStoredResyncMarker,
  type SseParsedEvent,
} from "@/lib/sse-fetch-client";
import { parseAlertPayload, parseFleetUpdatePayload, parseVehicleUpdatePayload } from "@/lib/sse-parser";
import { DECIMAL_EVENT_ID_PATTERN, parseStreamResetPayload } from "@/lib/sse-resync";
import { REALTIME_EVENTS } from "@/lib/realtime-events";
import type { FleetAlert, SseConnectionState, VehicleStatus } from "@/types/fleet";

type SseHandlers = {
  enabled?: boolean;
  /** Cambia al iniciar/cerrar sesión para forzar reconexión con credenciales actuales. */
  authToken?: string | null;
  onFleetUpdate?: (vehicles: VehicleStatus[]) => void;
  onAlert?: (alert: FleetAlert) => void;
  onStreamReset?: () => void | Promise<void>;
};

const RESYNC_RETRY_MS = 250;

/** Mantiene conexión SSE con fetch, JWT, Last-Event-ID y reconexión controlada. */
export function useSseStream({
  enabled = true,
  authToken = null,
  onFleetUpdate,
  onAlert,
  onStreamReset,
}: SseHandlers) {
  const [connectionState, setConnectionState] = useState<SseConnectionState>("disconnected");
  const handlersRef = useRef({ onFleetUpdate, onAlert, onStreamReset });
  handlersRef.current = { onFleetUpdate, onAlert, onStreamReset };
  const lastEventIdRef = useRef<string | null>(null);
  const previousTokenRef = useRef<string | null | undefined>(authToken);

  const connect = useCallback(() => {
    if (!enabled) {
      setConnectionState("disconnected");
      return () => setConnectionState("disconnected");
    }

    lastEventIdRef.current = readStoredLastEventId();

    const abortController = new AbortController();
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let reconnectAttempt = 0;
    let closed = false;
    let resyncRequired = readStoredResyncMarker();
    let pendingCutoverEventId: string | null = null;
    let snapshotCompletedThisSession = false;

    const scheduleReconnect = () => {
      if (closed) return;
      const delay = computeReconnectDelayMs(reconnectAttempt++);
      reconnectTimer = setTimeout(() => {
        void run();
      }, delay);
    };

    const persistLastEventId = (id: string) => {
      if (!DECIMAL_EVENT_ID_PATTERN.test(id)) return;
      lastEventIdRef.current = id;
      writeStoredLastEventId(id);
      writeStoredResyncMarker(false);
    };

    const clearLastEventId = () => {
      lastEventIdRef.current = null;
      writeStoredLastEventId(null);
    };

    const wait = (ms: number) => new Promise<void>((resolve) => {
      setTimeout(resolve, ms);
    });

    const completeResync = async () => {
      while (resyncRequired && !closed) {
        try {
          await handlersRef.current.onStreamReset?.();
          if (pendingCutoverEventId !== null) {
            persistLastEventId(pendingCutoverEventId);
            pendingCutoverEventId = null;
          }
          resyncRequired = false;
          snapshotCompletedThisSession = true;
          return;
        } catch {
          await wait(RESYNC_RETRY_MS);
        }
      }
    };

    const ensureResyncBeforeLive = async () => {
      if (snapshotCompletedThisSession) return true;
      if (!resyncRequired && readStoredResyncMarker()) {
        resyncRequired = true;
      }
      if (!resyncRequired) return true;
      await completeResync();
      return !resyncRequired;
    };

    const handleStreamReset = async (data: string) => {
      resyncRequired = true;
      const resetPayload = parseStreamResetPayload(data);
      clearLastEventId();
      if (resetPayload?.latestEventId) {
        pendingCutoverEventId = resetPayload.latestEventId;
      } else {
        pendingCutoverEventId = null;
        writeStoredResyncMarker(true);
      }
      await completeResync();
    };

    const handleEvent = async ({ event: eventName, data, id: eventId }: SseParsedEvent) => {
      if (eventName === REALTIME_EVENTS.connected) {
        reconnectAttempt = 0;
        setConnectionState("connected");
        if (!(await ensureResyncBeforeLive())) return;
        return;
      }

      if (eventName === REALTIME_EVENTS.heartbeat) {
        setConnectionState("connected");
        return;
      }

      if (eventName === REALTIME_EVENTS.streamReset) {
        await handleStreamReset(data);
        return;
      }

      if (!(await ensureResyncBeforeLive())) return;

      if (eventName === REALTIME_EVENTS.vehicleUpdate) {
        const vehicle = parseVehicleUpdatePayload(data);
        if (vehicle) {
          handlersRef.current.onFleetUpdate?.([vehicle]);
          if (eventId && DECIMAL_EVENT_ID_PATTERN.test(eventId)) persistLastEventId(eventId);
        }
        return;
      }

      if (eventName === REALTIME_EVENTS.fleetUpdate) {
        const vehicles = parseFleetUpdatePayload(data);
        if (vehicles && vehicles.length > 0) {
          handlersRef.current.onFleetUpdate?.(vehicles);
          if (eventId && DECIMAL_EVENT_ID_PATTERN.test(eventId)) persistLastEventId(eventId);
        }
        return;
      }

      if (eventName === REALTIME_EVENTS.alert) {
        const alert = parseAlertPayload(data);
        if (alert) {
          handlersRef.current.onAlert?.(alert);
          if (eventId && DECIMAL_EVENT_ID_PATTERN.test(eventId)) persistLastEventId(eventId);
        }
      }
    };

    const run = async () => {
      if (closed) return;
      setConnectionState("reconnecting");
      snapshotCompletedThisSession = false;
      resyncRequired = readStoredResyncMarker() || resyncRequired;

      try {
        const authStatus = await apiClient.fetchAuthStatus();
        const token = authToken ?? apiClient.getAuthToken();
        const headers = buildSseHeaders(
          authStatus.enabled,
          token,
          lastEventIdRef.current,
        );

        await consumeSseFetchStream(
          apiClient.getSseUrl(),
          { headers, signal: abortController.signal },
          {
            onOpen: () => setConnectionState("connected"),
            onEvent: handleEvent,
          },
        );

        if (!closed) scheduleReconnect();
      } catch (error) {
        if (abortController.signal.aborted || closed) return;

        if (isSseAuthError(error)) {
          setConnectionState("disconnected");
          return;
        }

        setConnectionState("reconnecting");
        scheduleReconnect();
      }
    };

    void run();

    return () => {
      closed = true;
      if (reconnectTimer) clearTimeout(reconnectTimer);
      abortController.abort();
      setConnectionState("disconnected");
    };
  }, [enabled, authToken]);

  useEffect(() => {
    if (previousTokenRef.current !== authToken) {
      lastEventIdRef.current = null;
      clearSseCursorState();
      previousTokenRef.current = authToken;
    }
  }, [authToken]);

  useEffect(() => connect(), [connect]);

  return { connectionState };
}
