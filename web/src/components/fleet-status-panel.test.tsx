/** @vitest-environment jsdom */
import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { FleetStatusPanel } from "@/components/fleet-status-panel";
import type { VehicleStatus } from "@/types/fleet";

function buildVehicles(count: number): VehicleStatus[] {
  return Array.from({ length: count }, (_, index) => ({
    vehicleId: `VH-${String(index + 1).padStart(3, "0")}`,
    name: `VH-${String(index + 1).padStart(3, "0")}`,
    deviceId: `11111111-1111-1111-1111-${String(index + 1).padStart(12, "0")}`,
    status: index % 2 === 0 ? "online" : "offline",
    lastSeenAt: "2026-07-10T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
    driverId: "Miguel",
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
        vehicleId: "VH-001",
        name: "VH-001",
        deviceId: "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
        status: "online",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: 40,
        lastLatitude: 4.6,
        lastLongitude: -74.0,
      },
      {
        vehicleId: "VH-002",
        name: "VH-002",
        deviceId: "bbbbbbbb-bbbb-4ccc-8ddd-eeeeeeeeeeee",
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

describe("FleetStatusPanel vehicle labels", () => {
  it("Panel_muestra_nombre_id_conductor_y_velocidad", () => {
    render(
      <FleetStatusPanel
        vehicles={[
          {
            vehicleId: "VH-001",
            name: "VH-001",
            deviceId: "df32fdsf-43gf-4r32-834f-4aaaaaaa0001",
            status: "online",
            lastSeenAt: "2026-07-10T01:23:00Z",
            lastSpeedKmh: 101,
            lastLatitude: 4.6,
            lastLongitude: -74.0,
            driverId: "Miguel",
          },
        ]}
      />,
    );

    expect(screen.getByText("VH-001")).toBeTruthy();
    expect(screen.getByText("ID: df32fdsf-43gf-4r32-834f-4aaaaaaa0001")).toBeTruthy();
    expect(screen.getByText("Conductor: Miguel")).toBeTruthy();
    expect(screen.getByText(/101 km\/h/i)).toBeTruthy();

    const card = screen.getByText("VH-001").closest("button");
    const text = card?.textContent ?? "";
    expect(text.indexOf("VH-001")).toBeLessThan(text.indexOf("ID:"));
    expect(text.indexOf("ID:")).toBeLessThan(text.indexOf("Conductor:"));
    expect(text.indexOf("Conductor:")).toBeLessThan(text.indexOf("101 km/h"));
  });
});
