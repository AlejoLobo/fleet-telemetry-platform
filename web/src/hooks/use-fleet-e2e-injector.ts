"use client";

import { useEffect } from "react";
import { isE2eTestMode } from "@/lib/e2e-test-mode";
import type { FleetAlert, VehicleStatus } from "@/types/fleet";

export type FleetE2eApi = {
  emitVehicleUpdate: (update: VehicleStatus) => void;
  emitAlert: (alert: FleetAlert) => void;
  getPendingVehicleCount: () => number;
};

declare global {
  interface Window {
    __FLEET_E2E__?: FleetE2eApi;
  }
}

type UseFleetE2eInjectorParams = {
  emitVehicleUpdate: (update: VehicleStatus) => void;
  emitAlert: (alert: FleetAlert) => void;
  getPendingVehicleCount: () => number;
};

/**
 * Expone window.__FLEET_E2E__ solo en build con NEXT_PUBLIC_E2E_TEST_MODE=true.
 * Conecta al mismo buffer SSE / manejador de alertas del dashboard.
 */
export function useFleetE2eInjector({
  emitVehicleUpdate,
  emitAlert,
  getPendingVehicleCount,
}: UseFleetE2eInjectorParams): void {
  useEffect(() => {
    if (!isE2eTestMode() || typeof window === "undefined") {
      return;
    }

    const api: FleetE2eApi = {
      emitVehicleUpdate,
      emitAlert,
      getPendingVehicleCount,
    };
    window.__FLEET_E2E__ = api;

    return () => {
      if (window.__FLEET_E2E__ === api) {
        delete window.__FLEET_E2E__;
      }
    };
  }, [emitVehicleUpdate, emitAlert, getPendingVehicleCount]);
}
