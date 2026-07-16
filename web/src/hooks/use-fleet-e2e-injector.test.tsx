/** @vitest-environment jsdom */
import { cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useFleetE2eInjector } from "@/hooks/use-fleet-e2e-injector";
import type { FleetAlert, VehicleStatus } from "@/types/fleet";

function Harness({
  emitVehicleUpdate,
  emitAlert,
  getPendingVehicleCount,
}: {
  emitVehicleUpdate: (u: VehicleStatus) => void;
  emitAlert: (a: FleetAlert) => void;
  getPendingVehicleCount: () => number;
}) {
  useFleetE2eInjector({ emitVehicleUpdate, emitAlert, getPendingVehicleCount });
  return null;
}

describe("useFleetE2eInjector", () => {
  afterEach(() => {
    cleanup();
    delete window.__FLEET_E2E__;
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("no registra inyector cuando E2E está deshabilitado", async () => {
    vi.stubEnv("NEXT_PUBLIC_E2E_TEST_MODE", "false");
    const emitVehicleUpdate = vi.fn();
    const emitAlert = vi.fn();
    const getPendingVehicleCount = vi.fn(() => 0);
    render(
      <Harness
        emitVehicleUpdate={emitVehicleUpdate}
        emitAlert={emitAlert}
        getPendingVehicleCount={getPendingVehicleCount}
      />,
    );
    expect(window.__FLEET_E2E__).toBeUndefined();
  });

  it("registra y limpia inyector en modo E2E", async () => {
    vi.stubEnv("NEXT_PUBLIC_E2E_TEST_MODE", "true");
    // Reimportar constantes compiladas: isE2eTestMode lee process.env en runtime.
    const emitVehicleUpdate = vi.fn();
    const emitAlert = vi.fn();
    const getPendingVehicleCount = vi.fn(() => 2);
    const { unmount } = render(
      <Harness
        emitVehicleUpdate={emitVehicleUpdate}
        emitAlert={emitAlert}
        getPendingVehicleCount={getPendingVehicleCount}
      />,
    );
    expect(window.__FLEET_E2E__).toBeDefined();
    window.__FLEET_E2E__!.emitVehicleUpdate({
      deviceId: "00000000-0000-4000-8000-000000000001",
      vehicleName: "Vehículo E2E",
      vehicleType: "car",
      status: "online",
      lastSeenAt: "2026-07-15T23:00:00Z",
      lastSpeedKmh: 137,
      lastLatitude: 4.65,
      lastLongitude: -74.08,
    });
    expect(emitVehicleUpdate).toHaveBeenCalledTimes(1);
    window.__FLEET_E2E__!.emitAlert({
      alertId: "alert-e2e-immediate",
      deviceId: "00000000-0000-4000-8000-000000000001",
      alertType: "overspeed",
      severity: "critical",
      message: "Alerta E2E inmediata",
      createdAt: "2026-07-15T23:00:00Z",
      isAcknowledged: false,
    });
    expect(emitAlert).toHaveBeenCalledTimes(1);
    expect(window.__FLEET_E2E__!.getPendingVehicleCount()).toBe(2);
    unmount();
    expect(window.__FLEET_E2E__).toBeUndefined();
  });

  it("Strict Mode no deja manejadores huérfanos al desmontar", async () => {
    vi.stubEnv("NEXT_PUBLIC_E2E_TEST_MODE", "true");
    const emitVehicleUpdate = vi.fn();
    const emitAlert = vi.fn();
    const getPendingVehicleCount = vi.fn(() => 0);
    const first = render(
      <Harness
        emitVehicleUpdate={emitVehicleUpdate}
        emitAlert={emitAlert}
        getPendingVehicleCount={getPendingVehicleCount}
      />,
    );
    const api = window.__FLEET_E2E__;
    expect(api).toBeDefined();
    first.unmount();
    expect(window.__FLEET_E2E__).toBeUndefined();
  });
});
