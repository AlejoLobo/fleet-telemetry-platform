/** @vitest-environment jsdom */
import React from "react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { KpiGrid } from "@/components/dashboard/kpi-grid";
import type { GlobalAnalytics } from "@/lib/analytics";

describe("KpiGrid analytics labels", () => {
  it("KpiGrid_renderiza_etiqueta_correcta_de_alertas", () => {
    const globalAnalytics: GlobalAnalytics = {
      activeVehicles: 2,
      totalVehicles: 3,
      openAlerts: 5,
      source: "TimescaleDB",
      partial: false,
      aggregationSource: "snapshot",
    };

    render(<KpiGrid globalAnalytics={globalAnalytics} selectedAnalytics={null} />);

    expect(screen.getByText("Alertas abiertas")).toBeTruthy();
    expect(screen.getByText("5")).toBeTruthy();
    expect(screen.queryByText(/críticas/i)).toBeNull();
  });

  it("KpiGrid_no_afirma_Ops_cuando_Ops_fallo", () => {
    const globalAnalytics: GlobalAnalytics = {
      activeVehicles: 2,
      totalVehicles: 3,
      openAlerts: 1,
      source: "TimescaleDB",
      partial: true,
      aggregationSource: "snapshot",
    };

    render(<KpiGrid globalAnalytics={globalAnalytics} selectedAnalytics={null} />);

    expect(screen.getAllByText(/métricas parciales del snapshot/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/Analítica parcial \(snapshot\)/i)).toBeTruthy();
    expect(screen.queryByText(/agregados globales Ops/i)).toBeNull();
  });

  it("KpiGrid_muestra_Ops_cuando_agregacion_es_ops", () => {
    const globalAnalytics: GlobalAnalytics = {
      activeVehicles: 8500,
      totalVehicles: 12000,
      openAlerts: 4,
      source: "TimescaleDB",
      partial: true,
      aggregationSource: "ops",
    };

    render(<KpiGrid globalAnalytics={globalAnalytics} selectedAnalytics={null} />);

    expect(screen.getAllByText(/agregados globales Ops/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/Analítica parcial \(agregados Ops\)/i)).toBeTruthy();
  });
});
