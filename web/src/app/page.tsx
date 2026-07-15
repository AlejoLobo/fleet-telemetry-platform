"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertCircle } from "lucide-react";

import { useFleetData } from "@/hooks/use-fleet-data";
import { useSseStream } from "@/hooks/use-sse-stream";
import { mergeVehicleUpdates, pruneVehiclePatches } from "@/lib/fleet-merge";
import { applyLocalConnectivity } from "@/lib/local-connectivity";
import { apiClient, ApiError } from "@/lib/api-client";
import { esSeveridadCritica } from "@/lib/labels";
import { mockDeviceId } from "@/mocks/fleet-data";
import {
  DEMO_REALTIME_REFRESH_MS,
  REALTIME_SELECTED_TELEMETRY_MS,
  loadMonitorRefreshRate,
  monitorRefreshRateToMs,
  saveMonitorRefreshRate,
  type MonitorRefreshRate,
} from "@/lib/monitor-refresh-rate";
import {
  bufferPendingVehicleUpdates,
  takePendingVehicleUpdates,
} from "@/lib/pending-vehicle-updates";

import { DashboardHeader } from "@/components/dashboard/dashboard-header";
import { KpiGrid } from "@/components/dashboard/kpi-grid";
import { FleetStatusPanel } from "@/components/fleet-status-panel";
import { FleetMapPanel } from "@/components/fleet-map-panel";
import { TelemetryTable } from "@/components/telemetry-table";
import { AiChatPanel } from "@/components/ai-chat-panel";
import { LoginPanel } from "@/components/auth/login-panel";
import { AlertsModal } from "@/components/alerts/alerts-modal";

import type { FleetAlert, VehicleStatus } from "@/types/fleet";
import type { MapFocusTarget } from "@/components/maps/leaflet-fleet-map";

const CONNECTIVITY_TICK_MS = 5_000;

export default function DashboardPage() {
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(mockDeviceId(0));
  const [liveVehiclePatches, setLiveVehiclePatches] = useState<VehicleStatus[]>([]);
  const [liveAlerts, setLiveAlerts] = useState<FleetAlert[]>([]);
  const [acknowledgingId, setAcknowledgingId] = useState<string | null>(null);
  const [authEnabled, setAuthEnabled] = useState(false);
  const [hasToken, setHasToken] = useState(false);
  const [authToken, setAuthToken] = useState<string | null>(null);
  const [authNotice, setAuthNotice] = useState<string | null>(null);
  const [alertsOpen, setAlertsOpen] = useState(false);
  const [alertsAttention, setAlertsAttention] = useState(false);
  const [mapAutoFit, setMapAutoFit] = useState(true);
  const [mapFocus, setMapFocus] = useState<MapFocusTarget | null>(null);
  const [connectivityNowMs, setConnectivityNowMs] = useState(() => Date.now());
  const [refreshRate, setRefreshRate] = useState<MonitorRefreshRate>(() =>
    loadMonitorRefreshRate(),
  );

  const pendingVehicleUpdatesRef = useRef<Map<string, VehicleStatus>>(new Map());
  const refreshRateRef = useRef(refreshRate);
  refreshRateRef.current = refreshRate;

  const refreshAuthState = async () => {
    try {
      const status = await apiClient.fetchAuthStatus();
      setAuthEnabled(status.enabled);
      setHasToken(apiClient.hasAuthToken());
      setAuthToken(apiClient.getAuthToken());
    } catch {
      setAuthEnabled(false);
      setAuthToken(null);
    }
  };

  useEffect(() => {
    void refreshAuthState();
  }, []);

  // Solo reloj local de frescura; no descarga datos ni depende del selector.
  useEffect(() => {
    setConnectivityNowMs(Date.now());
    const timer = window.setInterval(
      () => setConnectivityNowMs(Date.now()),
      CONNECTIVITY_TICK_MS,
    );
    return () => window.clearInterval(timer);
  }, []);

  const {
    vehicles,
    alerts,
    telemetry,
    globalAnalytics,
    selectedAnalytics,
    telemetryLoading,
    loading,
    error,
    dataSource,
    fleetTruncated,
    refresh,
    refreshSelectedTelemetry,
    refreshForResync,
    loadFromApi,
    loadDemoData,
  } = useFleetData(selectedDeviceId);

  const loadDemoDataRef = useRef(loadDemoData);
  loadDemoDataRef.current = loadDemoData;
  const refreshSelectedTelemetryRef = useRef(refreshSelectedTelemetry);
  refreshSelectedTelemetryRef.current = refreshSelectedTelemetry;

  const applyVehicleUpdates = useCallback((updates: VehicleStatus[]) => {
    if (updates.length === 0) return;
    setLiveVehiclePatches((prev) => mergeVehicleUpdates(prev, updates));
  }, []);

  const flushPendingVehicleUpdates = useCallback(() => {
    const pending = takePendingVehicleUpdates(pendingVehicleUpdatesRef.current);
    applyVehicleUpdates(pending);
  }, [applyVehicleUpdates]);

  const flushPendingVehicleUpdatesRef = useRef(flushPendingVehicleUpdates);
  flushPendingVehicleUpdatesRef.current = flushPendingVehicleUpdates;

  const { connectionState } = useSseStream({
    enabled: dataSource === "api",
    authToken,
    onFleetUpdate: (updates) => {
      if (refreshRateRef.current === "realtime") {
        applyVehicleUpdates(updates);
        return;
      }
      bufferPendingVehicleUpdates(pendingVehicleUpdatesRef.current, updates);
    },
    onAlert: (alert) => {
      setLiveAlerts((prev) => [alert, ...prev]);
      setAlertsAttention(true);
    },
    onStreamReset: async () => {
      pendingVehicleUpdatesRef.current.clear();
      setLiveVehiclePatches([]);
      setLiveAlerts([]);
      setAlertsAttention(false);
      const result = await refreshForResync(selectedDeviceId);
      setSelectedDeviceId(result.resolvedDeviceId);
    },
  });

  // Ciclo visual: buffer SSE + telemetría seleccionada (API) o regeneración demo.
  useEffect(() => {
    if (dataSource === "demo") {
      const ms =
        refreshRate === "realtime"
          ? DEMO_REALTIME_REFRESH_MS
          : (monitorRefreshRateToMs(refreshRate) ?? DEMO_REALTIME_REFRESH_MS);
      const timer = window.setInterval(() => {
        void loadDemoDataRef.current();
      }, ms);
      return () => window.clearInterval(timer);
    }

    if (dataSource !== "api") {
      return;
    }

    if (refreshRate === "realtime") {
      flushPendingVehicleUpdatesRef.current();
      const timer = window.setInterval(() => {
        void refreshSelectedTelemetryRef.current();
      }, REALTIME_SELECTED_TELEMETRY_MS);
      return () => window.clearInterval(timer);
    }

    const ms = monitorRefreshRateToMs(refreshRate);
    if (ms == null) return;

    const timer = window.setInterval(() => {
      flushPendingVehicleUpdatesRef.current();
      void refreshSelectedTelemetryRef.current();
    }, ms);
    return () => window.clearInterval(timer);
  }, [refreshRate, dataSource]);

  useEffect(() => {
    if (vehicles.length === 0) {
      if (selectedDeviceId !== null) setSelectedDeviceId(null);
      return;
    }

    if (!selectedDeviceId || !vehicles.some((v) => v.deviceId === selectedDeviceId)) {
      setSelectedDeviceId(vehicles[0].deviceId);
    }
  }, [vehicles, selectedDeviceId]);

  const resetLiveViewState = () => {
    pendingVehicleUpdatesRef.current.clear();
    setLiveVehiclePatches([]);
    setLiveAlerts([]);
    setAlertsAttention(false);
    setAuthNotice(null);
    setMapAutoFit(true);
    setMapFocus(null);
  };

  const handleLoadApi = async () => {
    resetLiveViewState();
    await loadFromApi();
  };

  const handleLoadDemo = async () => {
    resetLiveViewState();
    await loadDemoData();
  };

  const handleRefreshRateChange = (rate: MonitorRefreshRate) => {
    saveMonitorRefreshRate(rate);
    setRefreshRate(rate);
    if (rate === "realtime") {
      flushPendingVehicleUpdates();
    }
  };

  const handleManualRefresh = async () => {
    flushPendingVehicleUpdates();
    await refresh();
  };

  useEffect(() => {
    setLiveVehiclePatches((prev) => pruneVehiclePatches(prev, vehicles));
  }, [vehicles]);

  const displayVehicles = useMemo(() => {
    const merged =
      dataSource === "demo" || liveVehiclePatches.length === 0
        ? vehicles
        : mergeVehicleUpdates(vehicles, liveVehiclePatches);
    if (dataSource === "demo") return merged;
    return applyLocalConnectivity(merged, connectivityNowMs);
  }, [dataSource, liveVehiclePatches, vehicles, connectivityNowMs]);

  const displayAlerts = useMemo(() => {
    if (dataSource === "demo") return alerts;
    const merged = [...liveAlerts, ...alerts];
    const seen = new Set<string>();
    return merged.filter((a) => {
      if (seen.has(a.alertId)) return false;
      seen.add(a.alertId);
      return true;
    });
  }, [alerts, liveAlerts, dataSource]);

  const criticalAlertCount = displayAlerts.filter((a) => esSeveridadCritica(a.severity)).length;

  const handleFocusVehicle = (deviceId: string) => {
    setSelectedDeviceId(deviceId);
    setMapAutoFit(false);
    setMapFocus({ deviceId, tick: Date.now() });
  };

  const handleAcknowledgeAlert = async (alertId: string) => {
    if (dataSource === "demo") return;
    setAcknowledgingId(alertId);
    setAuthNotice(null);
    try {
      await apiClient.acknowledgeAlert(alertId);
      setLiveAlerts((prev) => prev.filter((a) => a.alertId !== alertId));
      await refresh();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setAuthNotice("Sesión expirada o sin permisos. Inicia sesión para confirmar alertas.");
      } else {
        setAuthNotice(err instanceof Error ? err.message : "No se pudo confirmar la alerta");
      }
    } finally {
      setAcknowledgingId(null);
    }
  };

  const selectedVehicle = displayVehicles.find((v) => v.deviceId === selectedDeviceId) ?? null;

  return (
    <div className="dashboard-grid-bg min-h-screen">
      <DashboardHeader
        loading={loading}
        dataSource={dataSource}
        connectionState={connectionState}
        alertCount={displayAlerts.length}
        criticalAlertCount={criticalAlertCount}
        alertsAttention={alertsAttention}
        refreshRate={refreshRate}
        onRefreshRateChange={handleRefreshRateChange}
        onOpenAlerts={() => {
          setAlertsAttention(false);
          setAlertsOpen(true);
        }}
        onLoadApi={handleLoadApi}
        onLoadDemo={handleLoadDemo}
        onRefresh={() => {
          void handleManualRefresh();
        }}
      />

      <AlertsModal
        open={alertsOpen}
        alerts={displayAlerts}
        vehicles={displayVehicles}
        onClose={() => setAlertsOpen(false)}
        onAcknowledge={dataSource === "api" ? handleAcknowledgeAlert : undefined}
        acknowledgingId={acknowledgingId}
      />

      <main className="mx-auto max-w-[1600px] space-y-6 px-4 py-6 md:px-6">
        {authEnabled && dataSource === "api" && (
          <section className="animate-fade-up">
            <LoginPanel
              hasToken={hasToken}
              onAuthChange={() => {
                setHasToken(apiClient.hasAuthToken());
                setAuthToken(apiClient.getAuthToken());
                setAuthNotice(null);
              }}
            />
          </section>
        )}

        {(error || authNotice) && (
          <div className="flex items-start gap-3 rounded-2xl border border-amber-200/80 bg-amber-50/90 px-4 py-3.5 text-sm text-amber-900 shadow-soft animate-fade-up">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
            <p>{authNotice ?? error}</p>
          </div>
        )}

        <section className="animate-fade-up">
          <KpiGrid
            globalAnalytics={globalAnalytics}
            selectedAnalytics={selectedAnalytics}
            telemetryLoading={telemetryLoading}
          />
        </section>

        <section className="grid gap-6 xl:grid-cols-12">
          <div className="xl:col-span-8">
            <FleetMapPanel
              vehicles={displayVehicles}
              selectedDeviceId={selectedDeviceId}
              focusTarget={mapFocus}
              autoFit={mapAutoFit}
            />
          </div>
          <div className="flex flex-col gap-6 xl:col-span-4">
            <FleetStatusPanel
              vehicles={displayVehicles}
              selectedDeviceId={selectedDeviceId}
              fleetTruncated={fleetTruncated}
              aggregationSource={globalAnalytics.aggregationSource}
              totalVehiclesGlobal={globalAnalytics.totalVehicles}
              activeVehiclesGlobal={globalAnalytics.activeVehicles}
              onSelectVehicle={setSelectedDeviceId}
              onFocusVehicle={handleFocusVehicle}
            />
          </div>
        </section>

        <section className="grid gap-6 lg:grid-cols-2">
          <TelemetryTable events={telemetry} vehicle={selectedVehicle} />
          <AiChatPanel useDemoResponses={dataSource === "demo"} />
        </section>
      </main>

      <footer className="border-t border-border bg-white py-4 text-center text-xs text-slate-500">
        Fleet Telemetry Platform · Monitoreo operativo en tiempo real
        <br />
        Powered By: Ing. Alejandro Lobo-Guerrero C.
      </footer>
    </div>
  );
}
