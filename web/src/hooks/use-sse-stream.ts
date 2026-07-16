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
import {
  DECIMAL_EVENT_ID_PATTERN,
  parseStreamResetPayload,
  ResyncSupersededError,
} from "@/lib/sse-resync";
import { REALTIME_EVENTS } from "@/lib/realtime-events";
import type { FleetAlert, NormalizedVehiclePatch, SseConnectionState, VehicleStatus } from "@/types/fleet";

type SseHandlers = {
  enabled?: boolean;
  /** Cambia al iniciar/cerrar sesión para forzar reconexión con credenciales actuales. */
  authToken?: string | null;
  onFleetUpdate?: (vehicles: Array<VehicleStatus | NormalizedVehiclePatch>) => void;
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
  const authTokenRef = useRef(authToken);
  authTokenRef.current = authToken;
  const activeConnectionIdRef = useRef(0);

  const connect = useCallback(() => {
    if (!enabled) {
      setConnectionState("disconnected");
      return () => setConnectionState("disconnected");
    }

    const connectionId = ++activeConnectionIdRef.current;
    const connectionToken = authToken;
    lastEventIdRef.current = readStoredLastEventId();

    const abortController = new AbortController();
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let reconnectAttempt = 0;
    let closed = false;
    let resyncRequired = readStoredResyncMarker();
    let pendingCutoverEventId: string | null = null;
    let snapshotCompletedThisSession = false;

    const isActiveConnection = () =>
      !closed
      && connectionId === activeConnectionIdRef.current
      && authTokenRef.current === connectionToken;

    const scheduleReconnect = () => {
      if (closed || !isActiveConnection()) return;
      const delay = computeReconnectDelayMs(reconnectAttempt++);
      reconnectTimer = setTimeout(() => {
        void run();
      }, delay);
    };

    const persistLastEventId = (id: string) => {
      if (!isActiveConnection()) return;
      if (!DECIMAL_EVENT_ID_PATTERN.test(id)) return;
      lastEventIdRef.current = id;
      writeStoredLastEventId(id);
      writeStoredResyncMarker(false);
    };

    const clearLastEventId = () => {
      if (!isActiveConnection()) return;
      lastEventIdRef.current = null;
      writeStoredLastEventId(null);
    };

    const wait = (ms: number) => new Promise<void>((resolve) => {
      setTimeout(resolve, ms);
    });

    const completeResync = async () => {
      while (resyncRequired && isActiveConnection()) {
        try {
          await handlersRef.current.onStreamReset?.();
          if (!isActiveConnection()) {
            // Conexión desmontada, abortada o reemplazada: no escribir storage ni estado.
            return;
          }

          if (pendingCutoverEventId !== null) {
            persistLastEventId(pendingCutoverEventId);
            pendingCutoverEventId = null;
          }
          // Con cutover null la marca permanece hasta el primer evento reproducible.
          resyncRequired = false;
          snapshotCompletedThisSession = true;
          return;
        } catch (error) {
          if (!isActiveConnection()) return;
          if (error instanceof ResyncSupersededError) {
            await wait(RESYNC_RETRY_MS);
            continue;
          }
          await wait(RESYNC_RETRY_MS);
        }
      }
    };

    const ensureResyncBeforeLive = async () => {
      if (!isActiveConnection()) return false;
      if (snapshotCompletedThisSession) return true;
      if (!resyncRequired && readStoredResyncMarker()) {
        resyncRequired = true;
      }
      if (!resyncRequired) return true;
      await completeResync();
      if (!isActiveConnection()) return false;
      return !resyncRequired;
    };

    const handleStreamReset = async (data: string) => {
      if (!isActiveConnection()) return;
      resyncRequired = true;
      writeStoredResyncMarker(true);
      const resetPayload = parseStreamResetPayload(data);
      clearLastEventId();
      pendingCutoverEventId = resetPayload?.latestEventId ?? null;
      await completeResync();
    };

    const handleEvent = async ({ event: eventName, data, id: eventId }: SseParsedEvent) => {
      if (!isActiveConnection()) return;

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
      if (!isActiveConnection()) return;

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
      if (!isActiveConnection()) return;
      setConnectionState("reconnecting");
      snapshotCompletedThisSession = false;
      resyncRequired = readStoredResyncMarker() || resyncRequired;

      try {
        const authStatus = await apiClient.fetchAuthStatus();
        if (!isActiveConnection()) return;
        const token = connectionToken ?? apiClient.getAuthToken();
        const headers = buildSseHeaders(
          authStatus.enabled,
          token,
          lastEventIdRef.current,
        );

        await consumeSseFetchStream(
          apiClient.getSseUrl(),
          { headers, signal: abortController.signal },
          {
            onOpen: () => {
              if (isActiveConnection()) setConnectionState("connected");
            },
            onEvent: handleEvent,
          },
        );

        if (isActiveConnection()) scheduleReconnect();
      } catch (error) {
        if (abortController.signal.aborted || !isActiveConnection()) return;

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
