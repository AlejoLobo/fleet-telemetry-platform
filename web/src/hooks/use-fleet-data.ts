"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import type { AnalyticsSummary, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { refreshMockDataset, getMockDataset } from "@/mocks/fleet-data";
import { getApiBaseUrl } from "@/lib/utils";

export type FleetDataSource = "api" | "demo";

type FleetDataState = {
  vehicles: VehicleStatus[];
  alerts: FleetAlert[];
  telemetry: TelemetryEvent[];
  analytics: AnalyticsSummary;
  loading: boolean;
  error: string | null;
  usingMock: boolean;
  dataSource: FleetDataSource;
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
  dataSource: FleetDataSource,
): AnalyticsSummary {
  const speeds = telemetry.map((t) => t.speedKmh);
  const avg = speeds.length ? speeds.reduce((a, b) => a + b, 0) / speeds.length : 0;

  return {
    averageSpeedKmh: Math.round(avg * 10) / 10,
    activeVehicles: vehicles.filter((v) => v.status === "online").length,
    totalVehicles: vehicles.length,
    openAlerts: alerts.length,
    source: dataSource === "demo" ? "Demostración" : "TimescaleDB",
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
    usingMock: false,
    dataSource: "api",
  });

  const dataSourceRef = useRef<FleetDataSource>("api");

  const loadFromApi = useCallback(async () => {
    setState((prev) => ({ ...prev, loading: true, error: null }));

    try {
      const vehicleId = selectedVehicleId ?? "VH-001";
      const [vehicles, alerts, telemetry] = await Promise.all([
        apiClient.fetchFleetLive(),
        apiClient.fetchAlertsLive(),
        apiClient.fetchTelemetryLive(vehicleId),
      ]);

      setState({
        vehicles,
        alerts,
        telemetry,
        analytics: computeAnalytics(vehicles, alerts, telemetry, "api"),
        loading: false,
        error:
          vehicles.length === 0
            ? "Sin vehículos en vivo. Solo aparecen si enviaron telemetría en los últimos 5 minutos. Usa modo Demo o publica eventos al API."
            : null,
        usingMock: false,
        dataSource: "api",
      });
      dataSourceRef.current = "api";
    } catch {
      setState((prev) => ({
        ...prev,
        loading: false,
        error: `No se pudo conectar con el backend (${getApiBaseUrl()}). Verifica que Docker, la API y el Worker estén activos.`,
        usingMock: false,
        dataSource: "api",
      }));
    }
  }, [selectedVehicleId]);

  const loadDemoData = useCallback(async () => {
    setState((prev) => ({ ...prev, loading: true, error: null }));

    const dataset = refreshMockDataset(6);
    const vehicleId =
      selectedVehicleId && dataset.vehicles.some((v) => v.vehicleId === selectedVehicleId)
        ? selectedVehicleId
        : (dataset.vehicles[0]?.vehicleId ?? "VH-001");
    const telemetry = dataset.telemetryByVehicle[vehicleId] ?? [];

    setState({
      vehicles: dataset.vehicles,
      alerts: dataset.alerts,
      telemetry,
      analytics: computeAnalytics(dataset.vehicles, dataset.alerts, telemetry, "demo"),
      loading: false,
      error: null,
      usingMock: true,
      dataSource: "demo",
    });
    dataSourceRef.current = "demo";
  }, [selectedVehicleId]);

  const refresh = useCallback(async () => {
    if (dataSourceRef.current === "demo") {
      await loadDemoData();
    } else {
      await loadFromApi();
    }
  }, [loadDemoData, loadFromApi]);

  useEffect(() => {
    loadFromApi();
  }, [loadFromApi]);

  useEffect(() => {
    if (dataSourceRef.current !== "demo" || !selectedVehicleId) return;
    const dataset = getMockDataset();
    const telemetryForVehicle = dataset.telemetryByVehicle[selectedVehicleId] ?? [];
    setState((prev) => {
      if (prev.dataSource !== "demo") return prev;
      return {
        ...prev,
        telemetry: telemetryForVehicle,
        analytics: computeAnalytics(
          prev.vehicles,
          prev.alerts,
          telemetryForVehicle,
          "demo",
        ),
      };
    });
  }, [selectedVehicleId]);

  return {
    ...state,
    refresh,
    loadFromApi,
    loadDemoData,
  };
}
