/** Hook para cargar y gestionar datos de la flota (API o demo). */
"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import { fetchTelemetrySnapshot } from "@/lib/fleet-pagination";
import {
  computeGlobalAnalytics,
  computeGlobalAnalyticsFromOps,
  computeSelectedAnalytics,
  toLegacyAnalytics,
  type FleetDataSource,
  type GlobalAnalytics,
  type SelectedVehicleAnalytics,
} from "@/lib/analytics";
import type { AnalyticsSummary, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { refreshMockDataset, getMockDataset } from "@/mocks/fleet-data";
import { getApiBaseUrl } from "@/lib/utils";
import { ResyncFailedError, type ResyncSnapshotResult } from "@/lib/sse-resync";

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
  fleetPartial: boolean;
  fleetTruncated: boolean;
  telemetryPartial: boolean;
  telemetryTruncated: boolean;
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
    fleetPartial: false,
    fleetTruncated: false,
    telemetryPartial: false,
    telemetryTruncated: false,
    dataSource: "api",
  });

  const dataSourceRef = useRef<FleetDataSource>("api");
  const telemetryAbortRef = useRef<AbortController | null>(null);
  const telemetryRequestIdRef = useRef(0);
  const snapshotGenerationRef = useRef(0);

  const loadFleetAndAlerts = useCallback(async () => {
    const generation = snapshotGenerationRef.current;
    setState((prev) => ({
      ...prev,
      fleetLoading: true,
      fleetError: null,
      fleetPartial: false,
      fleetTruncated: false,
    }));

    try {
      const [fleetSnapshot, alerts] = await Promise.all([
        apiClient.fetchFleetLive(),
        apiClient.fetchAlertsLive(),
      ]);

      let globalAnalytics;
      if (fleetSnapshot.truncated) {
        try {
          const summary = await apiClient.fetchOpsSummary();
          globalAnalytics = computeGlobalAnalyticsFromOps(
            {
              totalVehicles: summary.totalVehicles,
              activeVehicles: summary.activeVehicles,
            },
            alerts.length,
            "api",
            { partial: true },
          );
        } catch {
          globalAnalytics = computeGlobalAnalytics(fleetSnapshot.vehicles, alerts, "api", {
            partial: true,
            aggregationSource: "snapshot",
          });
        }
      } else {
        globalAnalytics = computeGlobalAnalytics(fleetSnapshot.vehicles, alerts, "api");
      }

      const fleetWarning = fleetSnapshot.truncated
        ? `Snapshot parcial: se muestran ${fleetSnapshot.vehicles.length} vehículos; existen más en el servidor.`
        : fleetSnapshot.partial
          ? fleetSnapshot.error ?? "Carga parcial de la flota."
          : null;

      if (generation !== snapshotGenerationRef.current) return;

      setState((prev) => ({
        ...prev,
        vehicles: fleetSnapshot.vehicles,
        alerts,
        globalAnalytics,
        fleetLoading: false,
        fleetPartial: fleetSnapshot.partial,
        fleetTruncated: fleetSnapshot.truncated,
        fleetError:
          fleetWarning ??
          (fleetSnapshot.vehicles.length === 0
            ? "Sin vehículos con telemetría. Publica eventos al API o usa modo Demo."
            : null),
        dataSource: "api",
      }));
      dataSourceRef.current = "api";
    } catch {
      if (generation !== snapshotGenerationRef.current) return;
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
    const generation = snapshotGenerationRef.current;

    setState((prev) => ({
      ...prev,
      telemetryLoading: true,
      telemetryError: null,
      telemetryPartial: false,
      telemetryTruncated: false,
    }));

    try {
      const snapshot = await fetchTelemetrySnapshot(vehicleId, { signal: controller.signal });
      if (requestId !== telemetryRequestIdRef.current) return;
      if (generation !== snapshotGenerationRef.current) return;

      const selectedAnalytics = computeSelectedAnalytics(vehicleId, snapshot.events);
      const telemetryWarning = snapshot.truncated
        ? `Historial parcial: se cargaron ${snapshot.events.length} eventos; existen más en el rango.`
        : snapshot.partial
          ? snapshot.error ?? "Carga parcial de telemetría."
          : null;

      setState((prev) => ({
        ...prev,
        telemetry: snapshot.events,
        selectedAnalytics,
        telemetryLoading: false,
        telemetryPartial: snapshot.partial,
        telemetryTruncated: snapshot.truncated,
        telemetryError: telemetryWarning,
      }));
    } catch (error) {
      if (error instanceof Error && error.name === "AbortError") return;
      if (requestId !== telemetryRequestIdRef.current) return;
      if (generation !== snapshotGenerationRef.current) return;

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
    setState((prev) => ({
      ...prev,
      fleetLoading: true,
      fleetError: null,
      telemetryError: null,
      fleetPartial: false,
      fleetTruncated: false,
      telemetryPartial: false,
      telemetryTruncated: false,
    }));

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
      fleetPartial: false,
      fleetTruncated: false,
      telemetryPartial: false,
      telemetryTruncated: false,
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

  const refreshForResync = useCallback(async (vehicleId: string): Promise<ResyncSnapshotResult> => {
    telemetryAbortRef.current?.abort();
    const generation = ++snapshotGenerationRef.current;

    if (dataSourceRef.current === "demo") {
      await loadDemoData();
      if (generation !== snapshotGenerationRef.current) {
        return { resolvedVehicleId: vehicleId || null };
      }
      return { resolvedVehicleId: vehicleId || null };
    }

    const [fleetSnapshot, alerts] = await Promise.all([
      apiClient.fetchFleetLive(),
      apiClient.fetchAlertsLive(),
    ]);

    if (generation !== snapshotGenerationRef.current) {
      return { resolvedVehicleId: null };
    }

    if (fleetSnapshot.partial && fleetSnapshot.error) {
      throw new ResyncFailedError(fleetSnapshot.error);
    }

    const resolvedVehicleId = fleetSnapshot.vehicles.some((v) => v.vehicleId === vehicleId)
      ? vehicleId
      : fleetSnapshot.vehicles[0]?.vehicleId ?? null;

    let telemetrySnapshot: Awaited<ReturnType<typeof fetchTelemetrySnapshot>> = {
      events: [],
      partial: false,
      truncated: false,
    };

    if (resolvedVehicleId) {
      telemetrySnapshot = await fetchTelemetrySnapshot(resolvedVehicleId);
      if (generation !== snapshotGenerationRef.current) {
        return { resolvedVehicleId };
      }
      if (telemetrySnapshot.partial && telemetrySnapshot.error) {
        throw new ResyncFailedError(telemetrySnapshot.error);
      }
    }

    let globalAnalytics;
    if (fleetSnapshot.truncated) {
      try {
        const summary = await apiClient.fetchOpsSummary();
        globalAnalytics = computeGlobalAnalyticsFromOps(
          {
            totalVehicles: summary.totalVehicles,
            activeVehicles: summary.activeVehicles,
          },
          alerts.length,
          "api",
          { partial: true },
        );
      } catch {
        globalAnalytics = computeGlobalAnalytics(fleetSnapshot.vehicles, alerts, "api", {
          partial: true,
          aggregationSource: "snapshot",
        });
      }
    } else {
      globalAnalytics = computeGlobalAnalytics(fleetSnapshot.vehicles, alerts, "api");
    }

    if (generation !== snapshotGenerationRef.current) {
      return { resolvedVehicleId };
    }

    const selectedAnalytics = resolvedVehicleId
      ? computeSelectedAnalytics(resolvedVehicleId, telemetrySnapshot.events)
      : null;

    setState((prev) => ({
      ...prev,
      vehicles: fleetSnapshot.vehicles,
      alerts,
      telemetry: telemetrySnapshot.events,
      globalAnalytics,
      selectedAnalytics,
      fleetLoading: false,
      telemetryLoading: false,
      fleetPartial: fleetSnapshot.partial,
      fleetTruncated: fleetSnapshot.truncated,
      telemetryPartial: telemetrySnapshot.partial,
      telemetryTruncated: telemetrySnapshot.truncated,
      fleetError: fleetSnapshot.vehicles.length === 0
        ? "Sin vehículos con telemetría. Publica eventos al API o usa modo Demo."
        : null,
      telemetryError: null,
      dataSource: "api",
    }));
    dataSourceRef.current = "api";

    return { resolvedVehicleId };
  }, [loadDemoData]);

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
    fleetPartial: state.fleetPartial,
    fleetTruncated: state.fleetTruncated,
    telemetryPartial: state.telemetryPartial,
    telemetryTruncated: state.telemetryTruncated,
    dataSource: state.dataSource,
    refresh,
    refreshForResync,
    loadFromApi,
    loadDemoData,
  };
}

export type { FleetDataSource };
export { ResyncFailedError } from "@/lib/sse-resync";
