/** Hook para cargar y gestionar datos de la flota (API o demo). */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import {
  computeGlobalAnalytics,
  computeSelectedAnalytics,
  toLegacyAnalytics,
  type FleetDataSource,
  type GlobalAnalytics,
  type SelectedVehicleAnalytics,
} from "@/lib/analytics";
import type { AnalyticsSummary, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { refreshMockDataset, getMockDataset } from "@/mocks/fleet-data";
import { getApiBaseUrl } from "@/lib/utils";

type FleetDataState = {
  vehicles: VehicleStatus[];
  alerts: FleetAlert[];
  telemetry: TelemetryEvent[];
  globalAnalytics: GlobalAnalytics;
  selectedAnalytics: SelectedVehicleAnalytics | null;
  fleetLoading: boolean;
  telemetryLoading: boolean;
  fleetError: string | null;
  telemetryError: string | null;
  dataSource: FleetDataSource;
};

const emptyGlobal: GlobalAnalytics = {
  activeVehicles: 0,
  totalVehicles: 0,
  openAlerts: 0,
  source: "TimescaleDB",
};

export function useFleetData(selectedVehicleId: string | null) {
  const [state, setState] = useState<FleetDataState>({
    vehicles: [],
    alerts: [],
    telemetry: [],
    globalAnalytics: emptyGlobal,
    selectedAnalytics: null,
    fleetLoading: true,
    telemetryLoading: false,
    fleetError: null,
    telemetryError: null,
    dataSource: "api",
  });

  const dataSourceRef = useRef<FleetDataSource>("api");
  const telemetryAbortRef = useRef<AbortController | null>(null);
  const telemetryRequestIdRef = useRef(0);

  const loadFleetAndAlerts = useCallback(async () => {
    setState((prev) => ({ ...prev, fleetLoading: true, fleetError: null }));

    try {
      const [vehicles, alerts] = await Promise.all([
        apiClient.fetchFleetLive(),
        apiClient.fetchAlertsLive(),
      ]);

      const globalAnalytics = computeGlobalAnalytics(vehicles, alerts, "api");

      setState((prev) => ({
        ...prev,
        vehicles,
        alerts,
        globalAnalytics,
        fleetLoading: false,
        fleetError:
          vehicles.length === 0
            ? "Sin vehículos con telemetría. Publica eventos al API o usa modo Demo."
            : null,
        dataSource: "api",
      }));
      dataSourceRef.current = "api";
    } catch {
      setState((prev) => ({
        ...prev,
        fleetLoading: false,
        fleetError: `No se pudo conectar con el backend (${getApiBaseUrl()}). Verifica que Docker, la API y el Worker estén activos.`,
        dataSource: "api",
      }));
    }
  }, []);

  const loadTelemetryForVehicle = useCallback(async (vehicleId: string) => {
    telemetryAbortRef.current?.abort();
    const controller = new AbortController();
    telemetryAbortRef.current = controller;
    const requestId = ++telemetryRequestIdRef.current;

    setState((prev) => ({ ...prev, telemetryLoading: true, telemetryError: null }));

    try {
      const to = new Date().toISOString();
      const from = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
      const response = await fetch(
        `${getApiBaseUrl()}/api/telemetry/${encodeURIComponent(vehicleId)}?from=${from}&to=${to}`,
        {
          signal: controller.signal,
          headers: { "Content-Type": "application/json" },
        },
      );

      if (!response.ok) throw new Error(`Error ${response.status} al cargar telemetría`);

      const telemetry = (await response.json()) as TelemetryEvent[];
      if (requestId !== telemetryRequestIdRef.current) return;

      const selectedAnalytics = computeSelectedAnalytics(vehicleId, telemetry);
      setState((prev) => ({
        ...prev,
        telemetry,
        selectedAnalytics,
        telemetryLoading: false,
      }));
    } catch (error) {
      if (error instanceof Error && error.name === "AbortError") return;
      if (requestId !== telemetryRequestIdRef.current) return;

      setState((prev) => ({
        ...prev,
        telemetryLoading: false,
        telemetryError: error instanceof Error ? error.message : "Error al cargar telemetría",
      }));
    }
  }, []);

  const loadFromApi = useCallback(async () => {
    await loadFleetAndAlerts();
    const vehicleId = selectedVehicleId ?? "VH-001";
    await loadTelemetryForVehicle(vehicleId);
  }, [loadFleetAndAlerts, loadTelemetryForVehicle, selectedVehicleId]);

  const loadDemoData = useCallback(async () => {
    telemetryAbortRef.current?.abort();
    setState((prev) => ({ ...prev, fleetLoading: true, fleetError: null, telemetryError: null }));

    const dataset = refreshMockDataset(10);
    const vehicleId =
      selectedVehicleId && dataset.vehicles.some((v) => v.vehicleId === selectedVehicleId)
        ? selectedVehicleId
        : (dataset.vehicles[0]?.vehicleId ?? "VH-001");
    const telemetry = dataset.telemetryByVehicle[vehicleId] ?? [];
    const globalAnalytics = computeGlobalAnalytics(dataset.vehicles, dataset.alerts, "demo");
    const selectedAnalytics = computeSelectedAnalytics(vehicleId, telemetry);

    setState({
      vehicles: dataset.vehicles,
      alerts: dataset.alerts,
      telemetry,
      globalAnalytics,
      selectedAnalytics,
      fleetLoading: false,
      telemetryLoading: false,
      fleetError: null,
      telemetryError: null,
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
    void loadFleetAndAlerts();
    return () => telemetryAbortRef.current?.abort();
  }, [loadFleetAndAlerts]);

  useEffect(() => {
    if (!selectedVehicleId) return;

    if (dataSourceRef.current === "demo") {
      const dataset = getMockDataset();
      const telemetryForVehicle = dataset.telemetryByVehicle[selectedVehicleId] ?? [];
      setState((prev) => {
        if (prev.dataSource !== "demo") return prev;
        return {
          ...prev,
          telemetry: telemetryForVehicle,
          selectedAnalytics: computeSelectedAnalytics(selectedVehicleId, telemetryForVehicle),
        };
      });
      return;
    }

    if (state.fleetLoading) return;
    void loadTelemetryForVehicle(selectedVehicleId);
  }, [selectedVehicleId, state.fleetLoading, loadTelemetryForVehicle]);

  const analytics: AnalyticsSummary = toLegacyAnalytics(
    state.globalAnalytics,
    state.selectedAnalytics,
  );

  return {
    vehicles: state.vehicles,
    alerts: state.alerts,
    telemetry: state.telemetry,
    analytics,
    globalAnalytics: state.globalAnalytics,
    selectedAnalytics: state.selectedAnalytics,
    loading: state.fleetLoading || state.telemetryLoading,
    fleetLoading: state.fleetLoading,
    telemetryLoading: state.telemetryLoading,
    error: state.fleetError ?? state.telemetryError,
    fleetError: state.fleetError,
    telemetryError: state.telemetryError,
    dataSource: state.dataSource,
    refresh,
    loadFromApi,
    loadDemoData,
  };
}

export type { FleetDataSource };
