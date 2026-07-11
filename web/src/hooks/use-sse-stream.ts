/** Hook SSE con backoff exponencial, jitter y soporte vehicle-update. */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import { normalizeVehicles } from "@/lib/fleet-normalize";
import type { FleetAlert, SseConnectionState, VehicleStatus } from "@/types/fleet";

type SseHandlers = {
  enabled?: boolean;
  onFleetUpdate?: (vehicles: VehicleStatus[]) => void;
  onVehicleUpdate?: (vehicle: VehicleStatus) => void;
  onAlert?: (alert: FleetAlert) => void;
};

const BASE_RECONNECT_MS = 1000;
const MAX_RECONNECT_MS = 30000;

function computeReconnectDelay(attempt: number): number {
  const exponential = Math.min(MAX_RECONNECT_MS, BASE_RECONNECT_MS * 2 ** attempt);
  const jitter = Math.floor(Math.random() * 500);
  return exponential + jitter;
}

export function useSseStream({
  enabled = true,
  onFleetUpdate,
  onVehicleUpdate,
  onAlert,
}: SseHandlers) {
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
        try {
          const vehicles = normalizeVehicles(JSON.parse(event.data) as VehicleStatus[]);
          if (vehicles.length > 0) handlersRef.current.onFleetUpdate?.(vehicles);
        } catch {
          /* payload inválido */
        }
      });

      eventSource.addEventListener("vehicle-update", (event) => {
        try {
          const vehicle = normalizeVehicles([JSON.parse(event.data) as VehicleStatus])[0];
          if (vehicle) handlersRef.current.onVehicleUpdate?.(vehicle);
        } catch {
          /* payload inválido */
        }
      });

      eventSource.addEventListener("alert", (event) => {
        try {
          handlersRef.current.onAlert?.(JSON.parse(event.data) as FleetAlert);
        } catch {
          /* payload inválido */
        }
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
