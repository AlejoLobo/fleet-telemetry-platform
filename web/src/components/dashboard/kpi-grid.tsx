/** Cuadrícula de indicadores clave (KPIs) del dashboard. */
import { Gauge, AlertTriangle, Truck, Activity } from "lucide-react";
import type { GlobalAnalytics, SelectedVehicleAnalytics } from "@/lib/analytics";
import { KpiCard } from "@/components/dashboard/kpi-card";
import { etiquetaFuenteAnalitica } from "@/lib/labels";

type KpiGridProps = {
  globalAnalytics: GlobalAnalytics;
  selectedAnalytics: SelectedVehicleAnalytics | null;
  telemetryLoading?: boolean;
};

export function KpiGrid({ globalAnalytics, selectedAnalytics, telemetryLoading }: KpiGridProps) {
  const onlinePercent =
    globalAnalytics.totalVehicles > 0
      ? Math.round((globalAnalytics.activeVehicles / globalAnalytics.totalVehicles) * 100)
      : 0;

  const selectedLabel = selectedAnalytics?.vehicleId ?? "—";
  const selectedSpeed = telemetryLoading
    ? "…"
    : String(selectedAnalytics?.averageSpeedKmh ?? 0);

  const fleetScopeSuffix =
    globalAnalytics.aggregationSource === "ops"
      ? " · agregados globales Ops"
      : globalAnalytics.partial
        ? " · métricas parciales del snapshot"
        : "";

  const alertsLabel = "Alertas abiertas";
  const alertsSublabel =
    globalAnalytics.openAlerts === 0
      ? `Sin incidentes${fleetScopeSuffix}`
      : `Requieren atención${fleetScopeSuffix}`;

  const sourceSublabel =
    globalAnalytics.aggregationSource === "ops"
      ? "Analítica parcial (agregados Ops)"
      : globalAnalytics.partial
        ? "Analítica parcial (snapshot)"
        : "Analítica operativa global";

  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <KpiCard
        icon={Gauge}
        label={`Velocidad · ${selectedLabel}`}
        value={selectedSpeed}
        sublabel="km/h · vehículo seleccionado · 24 h"
        accent="sky"
      />
      <KpiCard
        icon={Truck}
        label="Flota activa"
        value={`${globalAnalytics.activeVehicles}/${globalAnalytics.totalVehicles}`}
        sublabel={`${onlinePercent}% en línea · global${fleetScopeSuffix}`}
        accent="emerald"
      />
      <KpiCard
        icon={AlertTriangle}
        label={alertsLabel}
        value={String(globalAnalytics.openAlerts)}
        sublabel={alertsSublabel}
        accent="amber"
      />
      <KpiCard
        icon={Activity}
        label="Fuente de datos"
        value={etiquetaFuenteAnalitica(globalAnalytics.source)}
        sublabel={sourceSublabel}
        accent="violet"
      />
    </div>
  );
}
