/** Hook SSE con parser centralizado, backoff exponencial y vehicle-update. */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import { parseAlertPayload, parseFleetUpdatePayload, parseVehicleUpdatePayload } from "@/lib/sse-parser";
import { computeReconnectDelay } from "@/lib/sse-reconnect";
import type { FleetAlert, SseConnectionState, VehicleStatus } from "@/types/fleet";

type SseHandlers = {
  enabled?: boolean;
  onFleetUpdate?: (vehicles: VehicleStatus[]) => void;
  onVehicleUpdate?: (vehicle: VehicleStatus) => void;
  onAlert?: (alert: FleetAlert) => void;
};

export function useSseStream({ enabled = true, onFleetUpdate, onVehicleUpdate, onAlert }: SseHandlers) {
  const [connectionState, setConnectionState] = useState<SseConnectionState>("disconnected");
  const handlersRef = useRef({ onFleetUpdate, onVehicleUpdate, onAlert });
  handlersRef.current = { onFleetUpdate, onVehicleUpdate, onAlert };

  const connect = useCallback(() => {
    if (!enabled) {
      setConnectionState("disconnected");
      return () => setConnectionState("disconnected");
    }

    let eventSource: EventSource | null = null;
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let reconnectAttempt = 0;
    let closed = false;

    const open = () => {
      setConnectionState("reconnecting");
      eventSource = new EventSource(apiClient.getSseUrl());

      eventSource.addEventListener("connected", () => {
        reconnectAttempt = 0;
        setConnectionState("connected");
      });

      eventSource.addEventListener("fleet-update", (event) => {
        const vehicles = parseFleetUpdatePayload(event.data);
        if (vehicles !== null) handlersRef.current.onFleetUpdate?.(vehicles);
      });

      eventSource.addEventListener("vehicle-update", (event) => {
        const vehicle = parseVehicleUpdatePayload(event.data);
        if (vehicle) handlersRef.current.onVehicleUpdate?.(vehicle);
      });

      eventSource.addEventListener("alert", (event) => {
        const alert = parseAlertPayload(event.data);
        if (alert) handlersRef.current.onAlert?.(alert);
      });

      eventSource.addEventListener("heartbeat", () => {
        setConnectionState("connected");
      });

      eventSource.onerror = () => {
        setConnectionState("reconnecting");
        eventSource?.close();
        if (!closed) {
          reconnectTimer = setTimeout(open, computeReconnectDelay(reconnectAttempt++));
        }
      };
    };

    open();

    return () => {
      closed = true;
      if (reconnectTimer) clearTimeout(reconnectTimer);
      eventSource?.close();
      setConnectionState("disconnected");
    };
  }, [enabled]);

  useEffect(() => connect(), [connect]);

  return { connectionState };
}
