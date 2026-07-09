"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import { normalizeVehicles } from "@/lib/fleet-normalize";
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
        try {
          const vehicles = normalizeVehicles(JSON.parse(event.data) as VehicleStatus[]);
          if (vehicles.length > 0) {
            handlersRef.current.onFleetUpdate?.(vehicles);
          }
        } catch {
          /* ignorar payload inválido */
        }
      });
      eventSource.addEventListener("alert", (event) => {
        try {
          const alert = JSON.parse(event.data) as FleetAlert;
          handlersRef.current.onAlert?.(alert);
        } catch {
          /* ignorar payload inválido */
        }
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
