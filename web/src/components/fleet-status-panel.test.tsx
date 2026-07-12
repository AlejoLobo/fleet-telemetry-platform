/** @vitest-environment jsdom */
import React from "react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FleetStatusPanel } from "@/components/fleet-status-panel";
import type { VehicleStatus } from "@/types/fleet";

function buildVehicles(count: number): VehicleStatus[] {
  return Array.from({ length: count }, (_, index) => ({
    vehicleId: `VH-${String(index + 1).padStart(3, "0")}`,
    name: `VH-${String(index + 1).padStart(3, "0")}`,
    status: index % 2 === 0 ? "online" : "offline",
    lastSeenAt: "2026-07-10T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
  }));
}

describe("FleetStatusPanel truncated semantics", () => {
  it("Panel_no_presenta_5000_como_total_si_existen_12000", () => {
    render(
      <FleetStatusPanel
        vehicles={buildVehicles(5)}
        fleetTruncated
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
        status: "online",
        lastSeenAt: "2026-07-10T10:00:00Z",
        lastSpeedKmh: 40,
        lastLatitude: 4.6,
        lastLongitude: -74.0,
      },
      {
        vehicleId: "VH-002",
        name: "VH-002",
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
        totalVehiclesGlobal={12000}
        activeVehiclesGlobal={3800}
      />,
    );

    expect(screen.getAllByText("3.800/12.000").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/2 en línea dentro del snapshot mostrado/i).length).toBeGreaterThan(0);
    expect(screen.queryByText("2/12.000")).toBeNull();
  });
});
