"use client";

import { useEffect, useMemo, useState } from "react";
import { AlertCircle } from "lucide-react";
import { useFleetData } from "@/hooks/use-fleet-data";
import { useSseStream } from "@/hooks/use-sse-stream";
import { DashboardHeader } from "@/components/dashboard/dashboard-header";
import { KpiGrid } from "@/components/dashboard/kpi-grid";
import { FleetStatusPanel } from "@/components/fleet-status-panel";
import { FleetMapPanel } from "@/components/fleet-map-panel";
import { AlertsPanel } from "@/components/alerts-panel";
import { TelemetryTable } from "@/components/telemetry-table";
import { AiChatPanel } from "@/components/ai-chat-panel";
import { LoginPanel } from "@/components/auth/login-panel";
import { apiClient, ApiError } from "@/lib/api-client";
import type { FleetAlert, VehicleStatus } from "@/types/fleet";

export default function DashboardPage() {
  const [selectedVehicleId, setSelectedVehicleId] = useState<string>("VH-001");
  const [liveVehicles, setLiveVehicles] = useState<VehicleStatus[] | null>(null);
  const [liveAlerts, setLiveAlerts] = useState<FleetAlert[]>([]);
  const [acknowledgingId, setAcknowledgingId] = useState<string | null>(null);
  const [authEnabled, setAuthEnabled] = useState(false);
  const [hasToken, setHasToken] = useState(false);
  const [authNotice, setAuthNotice] = useState<string | null>(null);

  const refreshAuthState = async () => {
    try {
      const status = await apiClient.fetchAuthStatus();
      setAuthEnabled(status.enabled);
      setHasToken(apiClient.hasAuthToken());
    } catch {
      setAuthEnabled(false);
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
    loading,
    error,
    dataSource,
    refresh,
    loadFromApi,
    loadDemoData,
  } = useFleetData(selectedVehicleId);

  const { connectionState } = useSseStream({
    enabled: dataSource === "api",
    onFleetUpdate: setLiveVehicles,
    onAlert: (alert) => setLiveAlerts((prev) => [alert, ...prev]),
  });

  useEffect(() => {
    if (vehicles.length > 0 && !vehicles.some((v) => v.vehicleId === selectedVehicleId)) {
      setSelectedVehicleId(vehicles[0].vehicleId);
    }
  }, [vehicles, selectedVehicleId]);

  const handleLoadApi = async () => {
    setLiveVehicles(null);
    setLiveAlerts([]);
    setAuthNotice(null);
    await loadFromApi();
  };

  const handleLoadDemo = async () => {
    setLiveVehicles(null);
    setLiveAlerts([]);
    setAuthNotice(null);
    await loadDemoData();
  };

  const displayVehicles = dataSource === "api" ? (liveVehicles ?? vehicles) : vehicles;
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
        onLoadApi={handleLoadApi}
        onLoadDemo={handleLoadDemo}
        onRefresh={refresh}
      />

      <main className="mx-auto max-w-[1600px] space-y-6 px-4 py-6 md:px-6">
        {authEnabled && dataSource === "api" && (
          <section className="animate-fade-up">
            <LoginPanel
              hasToken={hasToken}
              onAuthChange={() => {
                setHasToken(apiClient.hasAuthToken());
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
          <KpiGrid analytics={analytics} />
        </section>

        <section className="grid gap-6 xl:grid-cols-12">
          <div className="xl:col-span-8">
            <FleetMapPanel
              vehicles={displayVehicles}
              selectedVehicleId={selectedVehicleId}
            />
          </div>
          <div className="flex flex-col gap-6 xl:col-span-4">
            <FleetStatusPanel
              vehicles={displayVehicles}
              selectedVehicleId={selectedVehicleId}
              onSelectVehicle={setSelectedVehicleId}
            />
            <AlertsPanel
              alerts={displayAlerts}
              onAcknowledge={dataSource === "api" ? handleAcknowledgeAlert : undefined}
              acknowledgingId={acknowledgingId}
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
      </footer>
    </div>
  );
}
