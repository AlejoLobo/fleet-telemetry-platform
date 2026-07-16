/** @vitest-environment jsdom */
import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { FleetStatusPanel } from "@/components/fleet-status-panel";
import type { VehicleStatus } from "@/types/fleet";

import { mockDeviceId } from "@/mocks/fleet-data";

function buildVehicles(count: number): VehicleStatus[] {
  return Array.from({ length: count }, (_, index) => ({
    deviceId: mockDeviceId(index),
    vehicleName: `VH-${String(index + 1).padStart(3, "0")}`,
    vehicleType: "car" as const,
    status: index % 2 === 0 ? "online" : "offline",
    lastSeenAt: "2026-07-10T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
  }));
}

afterEach(() => {
  cleanup();
});

describe("FleetStatusPanel truncated semantics", () => {
  it("Panel_truncado_con_Ops_muestra_globales", () => {
    render(
      <FleetStatusPanel
        vehicles={buildVehicles(5)}
        fleetTruncated
        aggregationSource="ops"
        totalVehiclesGlobal={12000}
        activeVehiclesGlobal={3800}
      />,
    );

    expect(screen.getAllByText(/5 mostrados de 12\.000/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText("3.800/12.000").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/agregados globales/i).length).toBeGreaterThan(0);
  });

  it("Panel_truncado_sin_Ops_no_inventa_total", () => {
    render(
      <FleetStatusPanel
        vehicles={buildVehicles(5)}
        fleetTruncated
        aggregationSource="snapshot"
        totalVehiclesGlobal={12000}
        activeVehiclesGlobal={3800}
      />,
    );

    expect(screen.getAllByText(/5 vehículos mostrados/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/total global no disponible/i).length).toBeGreaterThan(0);
    expect(screen.queryByText(/5 mostrados de 12\.000/i)).toBeNull();
    expect(screen.queryByText("3.800/12.000")).toBeNull();
    expect(screen.queryByText(/agregados globales/i)).toBeNull();
  });

  it("Panel_sin_Ops_no_muestra_agregados_globales", () => {
    render(
      <FleetStatusPanel
        vehicles={buildVehicles(3)}
        fleetTruncated={false}
        aggregationSource="snapshot"
        totalVehiclesGlobal={12000}
        activeVehiclesGlobal={3800}
      />,
    );

    expect(screen.getAllByText(/2 en línea · 3 total/i).length).toBeGreaterThan(0);
    expect(screen.queryByText(/agregados globales/i)).toBeNull();
    expect(screen.queryByText(/total global no disponible/i)).toBeNull();
  });

  it("Panel_no_presenta_5000_como_total_si_existen_12000", () => {
    render(
      <FleetStatusPanel
        vehicles={buildVehicles(5)}
        fleetTruncated
        aggregationSource="ops"
        totalVehiclesGlobal={12000}
        activeVehiclesGlobal={3800}
      />,
    );

    expect(screen.getAllByText(/5 mostrados de 12\.000/i).length).toBeGreaterThan(0);
    expect(screen.queryByText(/5 total/i)).toBeNull();
  });

  it("Panel_indica_subconjunto_mostrado", () => {
    render(
      <FleetStatusPanel
        vehicles={buildVehicles(3)}
        fleetTruncated
        aggregationSource="ops"
        totalVehiclesGlobal={12000}
        activeVehiclesGlobal={3800}
      />,
    );

    expect(screen.getAllByText(/3 mostrados de 12\.000/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/en línea dentro del snapshot mostrado/i).length).toBeGreaterThan(0);
  });

  it("Panel_no_mezcla_activos_locales_con_total_global", () => {
    const vehicles = [
      {
        deviceId: "00000000-0000-4000-8000-000000000001",
        vehicleName: "VH-001",
        vehicleType: "car" as const,
        status: "online",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: 40,
        lastLatitude: 4.6,
        lastLongitude: -74.0,
      },
      {
        deviceId: "00000000-0000-4000-8000-000000000002",
        vehicleName: "VH-002",
        vehicleType: "car" as const,
        status: "online",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: 40,
        lastLatitude: 4.6,
        lastLongitude: -74.0,
      },
    ];

    render(
      <FleetStatusPanel
        vehicles={vehicles}
        fleetTruncated
        aggregationSource="ops"
        totalVehiclesGlobal={12000}
        activeVehiclesGlobal={3800}
      />,
    );

    expect(screen.getAllByText("3.800/12.000").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/2 en línea dentro del snapshot mostrado/i).length).toBeGreaterThan(0);
    expect(screen.queryByText("2/12.000")).toBeNull();
  });
});

describe("FleetStatusPanel vehicle row format", () => {
  it("velocidad null muestra guión y cero muestra 0 km/h", () => {
    const vehicles: VehicleStatus[] = [
      {
        deviceId: "00000000-0000-4000-8000-000000000001",
        vehicleName: "VH-001",
        vehicleType: "car",
        status: "online",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: null,
        lastLatitude: 4.6,
        lastLongitude: -74.0,
      },
      {
        deviceId: "00000000-0000-4000-8000-000000000002",
        vehicleName: "VH-002",
        vehicleType: "van",
        status: "offline",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: 0,
        lastLatitude: null,
        lastLongitude: null,
      },
    ];

    render(<FleetStatusPanel vehicles={vehicles} />);

    expect(screen.getAllByText("—").length).toBeGreaterThan(0);
    expect(screen.getByText("0 km/h")).toBeTruthy();
    expect(screen.getByText("VH-001")).toBeTruthy();
    expect(screen.getByText("00000000-0000-4000-8000-000000000001")).toBeTruthy();
    expect(screen.getByLabelText("Tipo de vehículo: Van")).toBeTruthy();
    expect(screen.getByText("Automóvil")).toBeTruthy();
    expect(screen.getByText("Van")).toBeTruthy();
  });

  it("muestra iconos Lucide distintos por tipo en Demo", () => {
    const vehicles: VehicleStatus[] = [
      {
        deviceId: "00000000-0000-4000-8000-000000000005",
        vehicleName: "VH-005",
        vehicleType: "motorcycle",
        status: "online",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: 30,
        lastLatitude: 4.6,
        lastLongitude: -74.0,
      },
      {
        deviceId: "00000000-0000-4000-8000-000000000001",
        vehicleName: "VH-001",
        vehicleType: "truck",
        status: "online",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: 40,
        lastLatitude: 4.61,
        lastLongitude: -74.01,
      },
    ];

    render(<FleetStatusPanel vehicles={vehicles} />);

    expect(screen.getByLabelText("Tipo de vehículo: Motocicleta")).toBeTruthy();
    expect(screen.getByLabelText("Tipo de vehículo: Camión")).toBeTruthy();
    expect(screen.getByText("Motocicleta")).toBeTruthy();
    expect(screen.getByText("Camión")).toBeTruthy();
  });
});

describe("Dashboard page aggregation wiring", () => {
  it("Page_pasa_fuente_de_agregacion_al_panel", () => {
    render(
      <FleetStatusPanel
        vehicles={buildVehicles(4)}
        fleetTruncated
        aggregationSource="snapshot"
        totalVehiclesGlobal={9000}
        activeVehiclesGlobal={2500}
      />,
    );

    expect(screen.getAllByText(/métricas parciales del snapshot/i).length).toBeGreaterThan(0);
    expect(screen.queryByText(/agregados globales/i)).toBeNull();
  });
});
