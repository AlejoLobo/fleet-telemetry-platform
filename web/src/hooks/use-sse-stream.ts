/** Hook para conexión SSE con actualizaciones de flota y alertas. */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import { parseAlertPayload, parseFleetUpdatePayload } from "@/lib/sse-parser";
import type { FleetAlert, SseConnectionState, VehicleStatus } from "@/types/fleet";

type SseHandlers = {
  enabled?: boolean;
  onFleetUpdate?: (vehicles: VehicleStatus[]) => void;
  onAlert?: (alert: FleetAlert) => void;
};

export function useSseStream({ enabled = true, onFleetUpdate, onAlert }: SseHandlers) {
  const [connectionState, setConnectionState] = useState<SseConnectionState>("disconnected");
  const handlersRef = useRef({ onFleetUpdate, onAlert });
  handlersRef.current = { onFleetUpdate, onAlert };

  const connect = useCallback(() => {
    if (!enabled) {
      setConnectionState("disconnected");
      return () => setConnectionState("disconnected");
    }

    let eventSource: EventSource | null = null;
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let closed = false;

    const open = () => {
      setConnectionState("reconnecting");
      eventSource = new EventSource(apiClient.getSseUrl());

      eventSource.addEventListener("connected", () => {
        setConnectionState("connected");
      });

      eventSource.addEventListener("fleet-update", (event) => {
        const vehicles = parseFleetUpdatePayload(event.data);
        if (vehicles !== null) {
          handlersRef.current.onFleetUpdate?.(vehicles);
        }
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
          reconnectTimer = setTimeout(open, 3000);
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
