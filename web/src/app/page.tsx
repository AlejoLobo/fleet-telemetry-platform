"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertCircle } from "lucide-react";

import { useFleetData } from "@/hooks/use-fleet-data";
import { useSseStream } from "@/hooks/use-sse-stream";
import { mergeVehicleUpdates, pruneVehiclePatches } from "@/lib/fleet-merge";
import { resolveDisplayVehicles } from "@/lib/fleet-display";
import { apiClient, ApiError } from "@/lib/api-client";
import { esSeveridadCritica } from "@/lib/labels";

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

export default function DashboardPage() {
  const [selectedVehicleId, setSelectedVehicleId] = useState<string | null>("VH-001");
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
  /** Activo solo tras Actualizar: elimina desconectados previos sin ocultarlos en operación normal. */
  const [afterLiveRefresh, setAfterLiveRefresh] = useState(false);

  useEffect(() => {
    const timer = window.setInterval(() => setConnectivityNowMs(Date.now()), 5_000);
    return () => window.clearInterval(timer);
  }, []);

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

  const {
    vehicles,
    alerts,
    telemetry,
    analytics,
    globalAnalytics,
    selectedAnalytics,
    telemetryLoading,
    loading,
    error,
    dataSource,
    fleetTruncated,
    refresh,
    refreshForResync,
    loadFromApi,
    loadDemoData,
  } = useFleetData(selectedVehicleId);

  const { connectionState } = useSseStream({
    enabled: dataSource === "api",
    authToken,
    onFleetUpdate: (updates) => {
      setLiveVehiclePatches((prev) => mergeVehicleUpdates(prev, updates));
    },
    onAlert: (alert) => {
      setLiveAlerts((prev) => [alert, ...prev]);
      setAlertsAttention(true);
    },
    onStreamReset: async () => {
      setLiveVehiclePatches([]);
      setLiveAlerts([]);
      setAlertsAttention(false);
      const result = await refreshForResync(selectedVehicleId);
      setSelectedVehicleId(result.resolvedVehicleId);
    },
  });

  const resetLiveViewState = useCallback(() => {
    setLiveVehiclePatches([]);
    setLiveAlerts([]);
    setAlertsAttention(false);
    setAuthNotice(null);
    setMapAutoFit(true);
    setMapFocus(null);
  }, []);

  const handleLoadApi = async () => {
    resetLiveViewState();
    setAfterLiveRefresh(false);
    await loadFromApi();
  };

  const handleLoadDemo = async () => {
    resetLiveViewState();
    setAfterLiveRefresh(false);
    await loadDemoData();
  };

  /** Actualizar: limpia parches SSE y recarga solo vehículos dentro de la ventana online. */
  const handleManualRefresh = useCallback(async () => {
    resetLiveViewState();
    setAfterLiveRefresh(true);
    await refresh({ liveOnly: true });
  }, [refresh, resetLiveViewState]);

  useEffect(() => {
    setLiveVehiclePatches((prev) => pruneVehiclePatches(prev, vehicles));
  }, [vehicles]);

  const displayVehicles = useMemo(
    () =>
      resolveDisplayVehicles({
        vehicles,
        livePatches: liveVehiclePatches,
        dataSource,
        connectivityNowMs,
        afterLiveRefresh,
      }),
    [vehicles, liveVehiclePatches, dataSource, connectivityNowMs, afterLiveRefresh],
  );
  useEffect(() => {
    if (displayVehicles.length === 0) {
      if (selectedVehicleId !== null) setSelectedVehicleId(null);
      return;
    }

    if (!selectedVehicleId || !displayVehicles.some((v) => v.vehicleId === selectedVehicleId)) {
      setSelectedVehicleId(displayVehicles[0].vehicleId);
    }
  }, [displayVehicles, selectedVehicleId]);
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

  const handleFocusVehicle = (vehicleId: string) => {
    setSelectedVehicleId(vehicleId);
    setMapAutoFit(false);
    setMapFocus({ vehicleId, tick: Date.now() });
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

  return (
    <div className="dashboard-grid-bg min-h-screen">
      <DashboardHeader
        loading={loading}
        dataSource={dataSource}
        connectionState={connectionState}
        alertCount={displayAlerts.length}
        criticalAlertCount={criticalAlertCount}
        alertsAttention={alertsAttention}
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
              selectedVehicleId={selectedVehicleId}
              focusTarget={mapFocus}
              autoFit={mapAutoFit}
            />
          </div>
          <div className="flex flex-col gap-6 xl:col-span-4">
            <FleetStatusPanel
              vehicles={displayVehicles}
              selectedVehicleId={selectedVehicleId}
              fleetTruncated={fleetTruncated}
              aggregationSource={globalAnalytics.aggregationSource}
              totalVehiclesGlobal={globalAnalytics.totalVehicles}
              activeVehiclesGlobal={globalAnalytics.activeVehicles}
              onSelectVehicle={setSelectedVehicleId}
              onFocusVehicle={handleFocusVehicle}
            />
          </div>
        </section>

        <section className="grid gap-6 lg:grid-cols-2">
          <TelemetryTable events={telemetry} vehicleId={selectedVehicleId} />
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
