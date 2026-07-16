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
import { resolveFleetFetchError } from "@/lib/fleet-fetch-error";
import {
  ResyncFailedError,
  ResyncSupersededError,
  type ResyncSnapshotResult,
} from "@/lib/sse-resync";

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
  lastSuccessfulFleetAt: string | null;
};

const emptyGlobal: GlobalAnalytics = {
  activeVehicles: 0,
  totalVehicles: 0,
  openAlerts: 0,
  source: "TimescaleDB",
};

function findVehicleName(vehicles: VehicleStatus[], deviceId: string): string {
  return vehicles.find((v) => v.deviceId === deviceId)?.vehicleName ?? "";
}

export function useFleetData(selectedDeviceId: string | null) {
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
    lastSuccessfulFleetAt: null,
  });

  const dataSourceRef = useRef<FleetDataSource>("api");
  const telemetryAbortRef = useRef<AbortController | null>(null);
  const telemetryRequestIdRef = useRef(0);
  const snapshotGenerationRef = useRef(0);
  const vehiclesRef = useRef<VehicleStatus[]>([]);
  const selectedDeviceIdRef = useRef(selectedDeviceId);
  selectedDeviceIdRef.current = selectedDeviceId;

  const isCurrentGeneration = (generation: number) =>
    generation === snapshotGenerationRef.current;

  const loadFleetAndAlerts = useCallback(async (
    generation: number,
    options?: { silent?: boolean },
  ): Promise<boolean> => {
    setState((prev) => ({
      ...prev,
      fleetLoading: options?.silent ? prev.fleetLoading : true,
      fleetError: options?.silent ? prev.fleetError : null,
      fleetPartial: false,
      fleetTruncated: false,
    }));

    try {
      const [fleetSnapshot, alerts] = await Promise.all([
        apiClient.fetchFleetLive(),
        apiClient.fetchAlertsLive(),
      ]);

      if (!isCurrentGeneration(generation)) return false;

      let globalAnalytics;
      if (fleetSnapshot.truncated) {
        try {
          const summary = await apiClient.fetchOpsSummary();
          if (!isCurrentGeneration(generation)) return false;
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

      if (!isCurrentGeneration(generation)) return false;

      vehiclesRef.current = fleetSnapshot.vehicles;
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
        lastSuccessfulFleetAt: new Date().toISOString(),
      }));
      dataSourceRef.current = "api";
      return true;
    } catch (error) {
      if (!isCurrentGeneration(generation)) return false;
      const message = resolveFleetFetchError(error);
      setState((previous) => ({
        ...previous,
        fleetLoading: false,
        fleetError: message,
        dataSource: "api",
      }));
      return false;
    }
  }, []);

  const loadTelemetryForDevice = useCallback(async (
    deviceId: string,
    generation: number,
  ) => {
    if (!isCurrentGeneration(generation)) return;

    telemetryAbortRef.current?.abort();
    const controller = new AbortController();
    telemetryAbortRef.current = controller;
    const requestId = ++telemetryRequestIdRef.current;

    setState((prev) => ({
      ...prev,
      telemetryLoading: true,
      telemetryError: null,
      telemetryPartial: false,
      telemetryTruncated: false,
    }));

    try {
      const snapshot = await fetchTelemetrySnapshot(deviceId, { signal: controller.signal });
      if (requestId !== telemetryRequestIdRef.current) return;
      if (!isCurrentGeneration(generation)) return;

      const vehicleName = findVehicleName(vehiclesRef.current, deviceId);
      const selectedAnalytics = computeSelectedAnalytics(deviceId, snapshot.events, vehicleName);
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
      if (!isCurrentGeneration(generation)) return;

      const message = resolveFleetFetchError(error);
      setState((prev) => ({
        ...prev,
        telemetryLoading: false,
        telemetryError: message,
      }));
    }
  }, []);

  const clearSelectedTelemetry = useCallback(() => {
    telemetryAbortRef.current?.abort();
    setState((prev) => ({
      ...prev,
      telemetry: [],
      selectedAnalytics: null,
      telemetryLoading: false,
      telemetryError: null,
      telemetryPartial: false,
      telemetryTruncated: false,
    }));
  }, []);

  const loadFromApi = useCallback(async () => {
    const generation = ++snapshotGenerationRef.current;
    const fleetOk = await loadFleetAndAlerts(generation);
    if (!fleetOk || !isCurrentGeneration(generation)) return;

    const deviceId = selectedDeviceId
      && vehiclesRef.current.some((v) => v.deviceId === selectedDeviceId)
      ? selectedDeviceId
      : null;

    if (!deviceId) {
      clearSelectedTelemetry();
      return;
    }

    await loadTelemetryForDevice(deviceId, generation);
  }, [clearSelectedTelemetry, loadFleetAndAlerts, loadTelemetryForDevice, selectedDeviceId]);

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
    vehiclesRef.current = dataset.vehicles;
    const deviceId =
      selectedDeviceId && dataset.vehicles.some((v) => v.deviceId === selectedDeviceId)
        ? selectedDeviceId
        : (dataset.vehicles[0]?.deviceId ?? null);
    const telemetry = deviceId ? dataset.telemetryByDevice[deviceId] ?? [] : [];
    const globalAnalytics = computeGlobalAnalytics(dataset.vehicles, dataset.alerts, "demo");
    const selectedAnalytics = deviceId
      ? computeSelectedAnalytics(
          deviceId,
          telemetry,
          findVehicleName(dataset.vehicles, deviceId),
        )
      : null;

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
      lastSuccessfulFleetAt: new Date().toISOString(),
    });
    dataSourceRef.current = "demo";
  }, [selectedDeviceId]);

  const refresh = useCallback(async () => {
    if (dataSourceRef.current === "demo") {
      await loadDemoData();
    } else {
      await loadFromApi();
    }
  }, [loadDemoData, loadFromApi]);

  /** Solo historial del dispositivo seleccionado; no recarga flota ni alertas. */
  const refreshSelectedTelemetry = useCallback(async () => {
    if (dataSourceRef.current !== "api") return;

    const deviceId = selectedDeviceIdRef.current;
    if (!deviceId) return;
    if (!vehiclesRef.current.some((v) => v.deviceId === deviceId)) return;

    await loadTelemetryForDevice(deviceId, snapshotGenerationRef.current);
  }, [loadTelemetryForDevice]);

  const refreshForResync = useCallback(async (deviceId: string | null): Promise<ResyncSnapshotResult> => {
    telemetryAbortRef.current?.abort();
    const generation = ++snapshotGenerationRef.current;

    if (dataSourceRef.current === "demo") {
      await loadDemoData();
      if (!isCurrentGeneration(generation)) {
        throw new ResyncSupersededError();
      }
      return {
        resolvedDeviceId: deviceId
          && vehiclesRef.current.some((v) => v.deviceId === deviceId)
          ? deviceId
          : vehiclesRef.current[0]?.deviceId ?? null,
        applied: true,
      };
    }

    const [fleetSnapshot, alerts] = await Promise.all([
      apiClient.fetchFleetLive(),
      apiClient.fetchAlertsLive(),
    ]);

    if (!isCurrentGeneration(generation)) {
      throw new ResyncSupersededError();
    }

    if (fleetSnapshot.partial && fleetSnapshot.error) {
      throw new ResyncFailedError(fleetSnapshot.error);
    }

    const resolvedDeviceId = deviceId
      && fleetSnapshot.vehicles.some((v) => v.deviceId === deviceId)
      ? deviceId
      : fleetSnapshot.vehicles[0]?.deviceId ?? null;

    let telemetrySnapshot: Awaited<ReturnType<typeof fetchTelemetrySnapshot>> = {
      events: [],
      partial: false,
      truncated: false,
    };

    if (resolvedDeviceId) {
      telemetrySnapshot = await fetchTelemetrySnapshot(resolvedDeviceId);
      if (!isCurrentGeneration(generation)) {
        throw new ResyncSupersededError();
      }
      if (telemetrySnapshot.partial && telemetrySnapshot.error) {
        throw new ResyncFailedError(telemetrySnapshot.error);
      }
    }

    let globalAnalytics;
    if (fleetSnapshot.truncated) {
      try {
        const summary = await apiClient.fetchOpsSummary();
        if (!isCurrentGeneration(generation)) {
          throw new ResyncSupersededError();
        }
        globalAnalytics = computeGlobalAnalyticsFromOps(
          {
            totalVehicles: summary.totalVehicles,
            activeVehicles: summary.activeVehicles,
          },
          alerts.length,
          "api",
          { partial: true },
        );
      } catch (error) {
        if (error instanceof ResyncSupersededError) throw error;
        globalAnalytics = computeGlobalAnalytics(fleetSnapshot.vehicles, alerts, "api", {
          partial: true,
          aggregationSource: "snapshot",
        });
      }
    } else {
      globalAnalytics = computeGlobalAnalytics(fleetSnapshot.vehicles, alerts, "api");
    }

    if (!isCurrentGeneration(generation)) {
      throw new ResyncSupersededError();
    }

    const selectedAnalytics = resolvedDeviceId
      ? computeSelectedAnalytics(
          resolvedDeviceId,
          telemetrySnapshot.events,
          findVehicleName(fleetSnapshot.vehicles, resolvedDeviceId),
        )
      : null;

    vehiclesRef.current = fleetSnapshot.vehicles;
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
      lastSuccessfulFleetAt: new Date().toISOString(),
    }));
    dataSourceRef.current = "api";

    return { resolvedDeviceId, applied: true };
  }, [loadDemoData]);

  useEffect(() => {
    const generation = ++snapshotGenerationRef.current;
    void loadFleetAndAlerts(generation);
    return () => telemetryAbortRef.current?.abort();
  }, [loadFleetAndAlerts]);

  useEffect(() => {
    if (dataSourceRef.current === "demo") {
      if (!selectedDeviceId) {
        clearSelectedTelemetry();
        return;
      }
      const dataset = getMockDataset();
      const telemetryForDevice = dataset.telemetryByDevice[selectedDeviceId] ?? [];
      setState((prev) => {
        if (prev.dataSource !== "demo") return prev;
        return {
          ...prev,
          telemetry: telemetryForDevice,
          selectedAnalytics: computeSelectedAnalytics(
            selectedDeviceId,
            telemetryForDevice,
            findVehicleName(dataset.vehicles, selectedDeviceId),
          ),
        };
      });
      return;
    }

    if (state.fleetLoading) return;

    if (!selectedDeviceId
      || !state.vehicles.some((v) => v.deviceId === selectedDeviceId)) {
      clearSelectedTelemetry();
      return;
    }

    void loadTelemetryForDevice(selectedDeviceId, snapshotGenerationRef.current);
  }, [
    selectedDeviceId,
    state.fleetLoading,
    state.vehicles,
    loadTelemetryForDevice,
    clearSelectedTelemetry,
  ]);

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
    lastSuccessfulFleetAt: state.lastSuccessfulFleetAt,
    refresh,
    refreshSelectedTelemetry,
    refreshForResync,
    loadFromApi,
    loadDemoData,
  };
}

export type { FleetDataSource };
export { ResyncFailedError, ResyncSupersededError } from "@/lib/sse-resync";
