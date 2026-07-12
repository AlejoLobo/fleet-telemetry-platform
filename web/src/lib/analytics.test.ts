import { describe, expect, it } from "vitest";
import { computeGlobalAnalyticsFromOps } from "@/lib/analytics";

describe("KpiGrid truncated analytics", () => {
  it("muestra_agregados_globales_ops_cuando_snapshot_truncado", () => {
    const globalAnalytics = computeGlobalAnalyticsFromOps(
      { totalVehicles: 12000, activeVehicles: 8500, criticalAlerts: 12 },
      "api",
      { partial: true },
    );

    const fleetKpiValue = `${globalAnalytics.activeVehicles}/${globalAnalytics.totalVehicles}`;
    const partialSuffix = globalAnalytics.partial ? " · agregados globales Ops" : "";

    expect(fleetKpiValue).toBe("8500/12000");
    expect(globalAnalytics.openAlerts).toBe(12);
    expect(partialSuffix).toContain("agregados globales Ops");
    expect(globalAnalytics.source).toContain("Ops");
  });
});
