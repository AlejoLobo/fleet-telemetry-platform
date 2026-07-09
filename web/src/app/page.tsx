"use client";

import { useMemo, useState } from "react";
import { useFleetData } from "@/hooks/use-fleet-data";
import { useSseStream } from "@/hooks/use-sse-stream";
import { ConnectionStatus } from "@/components/connection-status";
import { FleetStatusPanel } from "@/components/fleet-status-panel";
import { FleetMapPanel } from "@/components/fleet-map-panel";
import { AlertsPanel } from "@/components/alerts-panel";
import { TelemetryTable } from "@/components/telemetry-table";
import { AnalyticsSummaryPanel } from "@/components/analytics-summary-panel";
import { AiChatPanel } from "@/components/ai-chat-panel";
import { Button } from "@/components/ui/button";
import type { FleetAlert, VehicleStatus } from "@/types/fleet";

export default function DashboardPage() {
  const [selectedVehicleId, setSelectedVehicleId] = useState<string>("VH-001");
  const [liveVehicles, setLiveVehicles] = useState<VehicleStatus[] | null>(null);
  const [liveAlerts, setLiveAlerts] = useState<FleetAlert[]>([]);

  const { vehicles, alerts, telemetry, analytics, loading, error, usingMock, refresh } =
    useFleetData(selectedVehicleId);

  const { connectionState } = useSseStream({
    onFleetUpdate: setLiveVehicles,
    onAlert: (alert) => setLiveAlerts((prev) => [alert, ...prev]),
  });

  const displayVehicles = liveVehicles ?? vehicles;
  const displayAlerts = useMemo(() => {
    const merged = [...liveAlerts, ...alerts];
    const seen = new Set<string>();
    return merged.filter((a) => {
      if (seen.has(a.alertId)) return false;
      seen.add(a.alertId);
      return true;
    });
  }, [alerts, liveAlerts]);

  return (
    <main className="min-h-screen bg-slate-50">
      <header className="border-b border-border bg-white">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-4 py-4">
          <div>
            <h1 className="text-xl font-bold">Fleet Telemetry Platform</h1>
            <p className="text-sm text-muted-foreground">Dashboard operativo — Fase 4</p>
          </div>
          <div className="flex items-center gap-3">
            <ConnectionStatus state={connectionState} usingMock={usingMock} />
            <Button variant="outline" onClick={refresh} disabled={loading}>
              Actualizar
            </Button>
          </div>
        </div>
      </header>

      <div className="mx-auto max-w-7xl space-y-4 p-4">
        {error && (
          <div className="rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900">
            {error}
          </div>
        )}

        <div className="grid gap-4 lg:grid-cols-3">
          <div className="lg:col-span-1">
            <AnalyticsSummaryPanel analytics={analytics} />
          </div>
          <div className="lg:col-span-2">
            <FleetMapPanel vehicles={displayVehicles} />
          </div>
        </div>

        <div className="grid gap-4 lg:grid-cols-2">
          <FleetStatusPanel vehicles={displayVehicles} />
          <AlertsPanel alerts={displayAlerts} />
        </div>

        <div className="flex flex-wrap gap-2">
          {displayVehicles.map((v) => (
            <Button
              key={v.vehicleId}
              variant={selectedVehicleId === v.vehicleId ? "default" : "outline"}
              onClick={() => setSelectedVehicleId(v.vehicleId)}
            >
              {v.vehicleId}
            </Button>
          ))}
        </div>

        <TelemetryTable events={telemetry} vehicleId={selectedVehicleId} />

        <div className="grid gap-4 lg:grid-cols-2">
          <div className="lg:col-span-1">
            <AiChatPanel />
          </div>
        </div>
      </div>
    </main>
  );
}
