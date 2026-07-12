/** Hook para conexión SSE autenticada vía fetch (FT-001). */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import {
  buildSseHeaders,
  computeReconnectDelayMs,
  consumeSseFetchStream,
  isSseAuthError,
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
};

/** Mantiene conexión SSE con fetch, JWT en Authorization y reconexión controlada. */
export function useSseStream({
  enabled = true,
  authToken = null,
  onFleetUpdate,
  onAlert,
}: SseHandlers) {
  const [connectionState, setConnectionState] = useState<SseConnectionState>("disconnected");
  const handlersRef = useRef({ onFleetUpdate, onAlert });
  handlersRef.current = { onFleetUpdate, onAlert };

  const connect = useCallback(() => {
    if (!enabled) {
      setConnectionState("disconnected");
      return () => setConnectionState("disconnected");
    }

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

    const handleEvent = (eventName: string, data: string) => {
      if (eventName === REALTIME_EVENTS.connected) {
        reconnectAttempt = 0;
        setConnectionState("connected");
        return;
      }

      if (eventName === REALTIME_EVENTS.heartbeat) {
        setConnectionState("connected");
        return;
      }

      if (eventName === REALTIME_EVENTS.vehicleUpdate) {
        const vehicle = parseVehicleUpdatePayload(data);
        if (vehicle) handlersRef.current.onFleetUpdate?.([vehicle]);
        return;
      }

      if (eventName === REALTIME_EVENTS.fleetUpdate) {
        const vehicles = parseFleetUpdatePayload(data);
        if (vehicles && vehicles.length > 0) handlersRef.current.onFleetUpdate?.(vehicles);
        return;
      }

      if (eventName === REALTIME_EVENTS.alert) {
        const alert = parseAlertPayload(data);
        if (alert) handlersRef.current.onAlert?.(alert);
      }
    };

    const run = async () => {
      if (closed) return;
      setConnectionState("reconnecting");

      try {
        const authStatus = await apiClient.fetchAuthStatus();
        const token = authToken ?? apiClient.getAuthToken();
        const headers = buildSseHeaders(authStatus.enabled, token);

        await consumeSseFetchStream(
          apiClient.getSseUrl(),
          { headers, signal: abortController.signal },
          {
            onOpen: () => setConnectionState("connected"),
            onEvent: ({ event, data }) => handleEvent(event, data),
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

  useEffect(() => connect(), [connect]);

  return { connectionState };
}
