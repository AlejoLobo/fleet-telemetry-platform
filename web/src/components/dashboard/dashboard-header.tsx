/** Cabecera del dashboard con controles de fuente de datos. */
import { Database, Dices, RefreshCw, Truck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ConnectionStatus } from "@/components/connection-status";
import { AlertsModalTrigger } from "@/components/alerts/alerts-modal";
import { cn } from "@/lib/utils";
import {
  MONITOR_REFRESH_RATE_OPTIONS,
  type MonitorRefreshRate,
  parseMonitorRefreshRate,
} from "@/lib/monitor-refresh-rate";
import type { SseConnectionState } from "@/types/fleet";

type DashboardHeaderProps = {
  loading: boolean;
  dataSource: "api" | "demo" | null;
  connectionState: SseConnectionState;
  alertCount: number;
  criticalAlertCount: number;
  alertsAttention?: boolean;
  refreshRate: MonitorRefreshRate;
  onRefreshRateChange: (rate: MonitorRefreshRate) => void;
  onOpenAlerts: () => void;
  onLoadApi: () => void;
  onLoadDemo: () => void;
  onRefresh: () => void;
};

/** Barra superior con título, conexión y acciones. */
export function DashboardHeader({
  loading,
  dataSource,
  connectionState,
  alertCount,
  criticalAlertCount,
  alertsAttention = false,
  refreshRate,
  onRefreshRateChange,
  onOpenAlerts,
  onLoadApi,
  onLoadDemo,
  onRefresh,
}: DashboardHeaderProps) {
  return (
    <header className="sticky top-0 z-50 border-b border-border glass-panel">
      <div className="mx-auto flex max-w-[1600px] flex-col gap-4 px-4 py-4 md:px-6 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex items-start gap-4">
          <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-primary to-violet-500 text-white shadow-glow">
            <Truck className="h-6 w-6" />
          </div>
          <div>
            <div className="mb-1 flex items-center gap-2">
              <span className="relative flex h-2 w-2">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
              </span>
              <span className="text-xs font-semibold uppercase tracking-widest text-primary">
                Monitoreo en vivo
              </span>
            </div>
            <h1 className="text-xl font-bold text-gradient md:text-2xl">
              Plataforma de Telemetría de Flotas
            </h1>
            <p className="text-sm text-slate-500">Centro de control operativo · Tiempo real</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2 lg:justify-end">
          <ConnectionStatus state={connectionState} dataSource={dataSource} />
          <AlertsModalTrigger
            alertCount={alertCount}
            criticalCount={criticalAlertCount}
            attention={alertsAttention}
            onClick={onOpenAlerts}
          />
          <div className="hidden h-6 w-px bg-slate-200 sm:block" />
          <Button
            variant={dataSource === "api" ? "default" : "outline"}
            size="sm"
            onClick={onLoadApi}
            disabled={loading}
          >
            <Database className="h-4 w-4" />
            Tiempo real
          </Button>
          <Button
            variant={dataSource === "demo" ? "default" : "outline"}
            size="sm"
            onClick={onLoadDemo}
            disabled={loading}
          >
            <Dices className="h-4 w-4" />
            Demo
          </Button>
          <label className="flex items-center gap-1.5 text-xs text-slate-600">
            <span className="whitespace-nowrap font-medium">Actualización</span>
            <select
              aria-label="Actualización"
              title="Los datos se reciben continuamente; esta opción controla cuándo se actualiza la pantalla."
              className="h-8 max-w-[11rem] rounded-md border border-input bg-background px-2 text-xs text-slate-800 shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              value={String(refreshRate)}
              onChange={(event) => {
                onRefreshRateChange(parseMonitorRefreshRate(event.target.value));
              }}
            >
              {MONITOR_REFRESH_RATE_OPTIONS.map((option) => (
                <option key={String(option.value)} value={String(option.value)}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <Button variant="outline" size="sm" onClick={onRefresh} disabled={loading}>
            <RefreshCw className={cn("h-4 w-4", loading && "animate-spin")} />
            Actualizar
          </Button>
        </div>
      </div>
    </header>
  );
}
