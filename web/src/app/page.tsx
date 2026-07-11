/** Página principal del dashboard de telemetría de flotas. */
"use client";

import { useEffect, useState } from "react";
import { AlertCircle } from "lucide-react";
import { useFleetData } from "@/hooks/use-fleet-data";
import { useSseStream } from "@/hooks/use-sse-stream";
import { useDashboardAuth } from "@/hooks/use-dashboard-auth";
import { useDisplayFleet } from "@/hooks/use-display-fleet";
import { useDisplayAlerts } from "@/hooks/use-display-alerts";
import { DashboardHeader } from "@/components/dashboard/dashboard-header";
import { KpiGrid } from "@/components/dashboard/kpi-grid";
import { FleetStatusPanel } from "@/components/fleet-status-panel";
import { FleetMapPanel } from "@/components/fleet-map-panel";
import { TelemetryTable } from "@/components/telemetry-table";
import { AiChatPanel } from "@/components/ai-chat-panel";
import { LoginPanel } from "@/components/auth/login-panel";
import { AlertsModal } from "@/components/alerts/alerts-modal";
import { apiClient, ApiError } from "@/lib/api-client";
import { esSeveridadCritica } from "@/lib/labels";
import type { FleetAlert, VehicleStatus } from "@/types/fleet";
import type { MapFocusTarget } from "@/components/maps/leaflet-fleet-map";

export default function DashboardPage() {
  const [selectedVehicleId, setSelectedVehicleId] = useState<string>("VH-001");
  const [liveVehicles, setLiveVehicles] = useState<VehicleStatus[] | null>(null);
  const [liveAlerts, setLiveAlerts] = useState<FleetAlert[]>([]);
  const [acknowledgingId, setAcknowledgingId] = useState<string | null>(null);
  const [alertsOpen, setAlertsOpen] = useState(false);
  const [alertsAttention, setAlertsAttention] = useState(false);
  const [mapAutoFit, setMapAutoFit] = useState(true);
  const [mapFocus, setMapFocus] = useState<MapFocusTarget | null>(null);

  const { authEnabled, hasToken, authNotice, setAuthNotice, onAuthChange } = useDashboardAuth();

  const {
    vehicles,
    alerts,
    telemetry,
    globalAnalytics,
    selectedAnalytics,
    fleetLoading,
    telemetryLoading,
    fleetError,
    telemetryError,
    dataSource,
    refresh,
    loadFromApi,
    loadDemoData,
  } = useFleetData(selectedVehicleId);

  const { connectionState } = useSseStream({
    enabled: dataSource === "api",
    onFleetUpdate: setLiveVehicles,

    onVehicleUpdate: (vehicle) => {
      setLiveVehicles((prev) => {
        const base = prev ?? vehicles;
        const index = base.findIndex((v) => v.vehicleId === vehicle.vehicleId);
        if (index < 0) return [...base, vehicle];
        const next = [...base];
        next[index] = { ...next[index], ...vehicle };
        return next;
      });
    },

    onAlert: (alert) => {
      setLiveAlerts((prev) => [alert, ...prev]);
      setAlertsAttention(true);
    },
  });

  const displayVehicles = useDisplayFleet(dataSource, vehicles, liveVehicles);
  const displayAlerts = useDisplayAlerts(dataSource, alerts, liveAlerts);
  const criticalAlertCount = displayAlerts.filter((a) => esSeveridadCritica(a.severity)).length;

  useEffect(() => {
    if (displayVehicles.length > 0 && !displayVehicles.some((v) => v.vehicleId === selectedVehicleId)) {
      setSelectedVehicleId(displayVehicles[0].vehicleId);
    }
  }, [displayVehicles, selectedVehicleId]);

  const resetLiveState = () => {
    setLiveVehicles(null);
    setLiveAlerts([]);
    setAlertsAttention(false);
    setMapAutoFit(true);
    setMapFocus(null);
  };

  const handleLoadApi = async () => {
    resetLiveState();
    setAuthNotice(null);
    await loadFromApi();
  };

  const handleLoadDemo = async () => {
    resetLiveState();
    setAuthNotice(null);
    await loadDemoData();
  };

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

  const bannerMessage = authNotice ?? fleetError ?? telemetryError;

  return (
    <div className="dashboard-grid-bg min-h-screen">
      <DashboardHeader
        loading={fleetLoading}
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
        onRefresh={refresh}
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
            <LoginPanel hasToken={hasToken} onAuthChange={onAuthChange} />
          </section>
        )}

        {bannerMessage && (
          <div className="flex items-start gap-3 rounded-2xl border border-amber-200/80 bg-amber-50/90 px-4 py-3.5 text-sm text-amber-900 shadow-soft animate-fade-up">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
            <p>{bannerMessage}</p>
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
