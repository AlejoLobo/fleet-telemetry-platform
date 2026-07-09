"use client";

import { useCallback, useEffect, useState } from "react";
import { apiClient } from "@/lib/api-client";
import type { AnalyticsSummary, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { mockAlerts, mockTelemetry, mockVehicles } from "@/mocks/fleet-data";
import { isMockMode } from "@/lib/utils";

type FleetDataState = {
  vehicles: VehicleStatus[];
  alerts: FleetAlert[];
  telemetry: TelemetryEvent[];
  analytics: AnalyticsSummary;
  loading: boolean;
  error: string | null;
  usingMock: boolean;
};

const defaultAnalytics: AnalyticsSummary = {
  averageSpeedKmh: 0,
  activeVehicles: 0,
  totalVehicles: 0,
  openAlerts: 0,
  source: "TimescaleDB",
};

function computeAnalytics(
  vehicles: VehicleStatus[],
  alerts: FleetAlert[],
  telemetry: TelemetryEvent[],
): AnalyticsSummary {
  const speeds = telemetry.map((t) => t.speedKmh);
  const avg = speeds.length ? speeds.reduce((a, b) => a + b, 0) / speeds.length : 38.2;

  return {
    averageSpeedKmh: Math.round(avg * 10) / 10,
    activeVehicles: vehicles.filter((v) => v.status === "online").length,
    totalVehicles: vehicles.length,
    openAlerts: alerts.length,
    source: isMockMode() ? "Mock (Druid MVP)" : "TimescaleDB (mock Druid MVP)",
  };
}

export function useFleetData(selectedVehicleId: string | null) {
  const [state, setState] = useState<FleetDataState>({
    vehicles: [],
    alerts: [],
    telemetry: [],
    analytics: defaultAnalytics,
    loading: true,
    error: null,
    usingMock: isMockMode(),
  });

  const refresh = useCallback(async () => {
    setState((prev) => ({ ...prev, loading: true, error: null }));

    try {
      const vehicleId = selectedVehicleId ?? "VH-001";
      const [vehicles, alerts, telemetry] = await Promise.all([
        apiClient.getFleet(),
        apiClient.getAlerts(),
        apiClient.getTelemetry(vehicleId),
      ]);

      setState({
        vehicles,
        alerts,
        telemetry,
        analytics: computeAnalytics(vehicles, alerts, telemetry),
        loading: false,
        error: null,
        usingMock: isMockMode(),
      });
    } catch {
      const vehicles = mockVehicles;
      const alerts = mockAlerts;
      const telemetry = mockTelemetry;
      setState({
        vehicles,
        alerts,
        telemetry,
        analytics: computeAnalytics(vehicles, alerts, telemetry),
        loading: false,
        error: "Backend no disponible — mostrando datos mock.",
        usingMock: true,
      });
    }
  }, [selectedVehicleId]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  return { ...state, refresh };
}
