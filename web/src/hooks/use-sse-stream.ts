/** Hook para conexión SSE autenticada vía fetch (FT-001 / FT-005). */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import {
  buildSseHeaders,
  computeReconnectDelayMs,
  consumeSseFetchStream,
  isSseAuthError,
  readStoredLastEventId,
  writeStoredLastEventId,
} from "@/lib/sse-fetch-client";
import { parseAlertPayload, parseFleetUpdatePayload, parseVehicleUpdatePayload } from "@/lib/sse-parser";
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

function shouldTrackEventId(eventName: string): boolean {
  return eventName === REALTIME_EVENTS.vehicleUpdate
    || eventName === REALTIME_EVENTS.fleetUpdate
    || eventName === REALTIME_EVENTS.alert;
}

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

    const scheduleReconnect = () => {
      if (closed) return;
      const delay = computeReconnectDelayMs(reconnectAttempt++);
      reconnectTimer = setTimeout(() => {
        void run();
      }, delay);
    };

    const persistLastEventId = (id: string) => {
      lastEventIdRef.current = id;
      writeStoredLastEventId(id);
    };

    const clearLastEventId = () => {
      lastEventIdRef.current = null;
      writeStoredLastEventId(null);
    };

    const handleEvent = async (eventName: string, data: string, eventId?: string) => {
      if (eventName === REALTIME_EVENTS.connected) {
        reconnectAttempt = 0;
        setConnectionState("connected");
        return;
      }

      if (eventName === REALTIME_EVENTS.heartbeat) {
        setConnectionState("connected");
        return;
      }

      if (eventName === REALTIME_EVENTS.streamReset) {
        clearLastEventId();
        await handlersRef.current.onStreamReset?.();
        return;
      }

      if (eventName === REALTIME_EVENTS.vehicleUpdate) {
        const vehicle = parseVehicleUpdatePayload(data);
        if (vehicle) {
          handlersRef.current.onFleetUpdate?.([vehicle]);
          if (eventId) persistLastEventId(eventId);
        }
        return;
      }

      if (eventName === REALTIME_EVENTS.fleetUpdate) {
        const vehicles = parseFleetUpdatePayload(data);
        if (vehicles && vehicles.length > 0) {
          handlersRef.current.onFleetUpdate?.(vehicles);
          if (eventId) persistLastEventId(eventId);
        }
        return;
      }

      if (eventName === REALTIME_EVENTS.alert) {
        const alert = parseAlertPayload(data);
        if (alert) {
          handlersRef.current.onAlert?.(alert);
          if (eventId) persistLastEventId(eventId);
        }
      }
    };

    const run = async () => {
      if (closed) return;
      setConnectionState("reconnecting");

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
            onEvent: async ({ event, data, id }) => {
              await handleEvent(event, data, shouldTrackEventId(event) ? id : undefined);
            },
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
      writeStoredLastEventId(null);
      previousTokenRef.current = authToken;
    }
  }, [authToken]);

  useEffect(() => connect(), [connect]);

  return { connectionState };
}
