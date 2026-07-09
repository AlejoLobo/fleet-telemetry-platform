import { Gauge, AlertTriangle, Truck, Activity } from "lucide-react";
import type { AnalyticsSummary } from "@/types/fleet";
import { KpiCard } from "@/components/dashboard/kpi-card";
import { etiquetaFuenteAnalitica } from "@/lib/labels";

export function KpiGrid({ analytics }: { analytics: AnalyticsSummary }) {
  const onlinePercent =
    analytics.totalVehicles > 0
      ? Math.round((analytics.activeVehicles / analytics.totalVehicles) * 100)
      : 0;

  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <KpiCard
        icon={Gauge}
        label="Velocidad promedio"
        value={`${analytics.averageSpeedKmh}`}
        sublabel="km/h · últimas 24 h"
        accent="sky"
      />
      <KpiCard
        icon={Truck}
        label="Flota activa"
        value={`${analytics.activeVehicles}/${analytics.totalVehicles}`}
        sublabel={`${onlinePercent}% en línea`}
        accent="emerald"
      />
      <KpiCard
        icon={AlertTriangle}
        label="Alertas abiertas"
        value={String(analytics.openAlerts)}
        sublabel={analytics.openAlerts === 0 ? "Sin incidentes" : "Requieren atención"}
        accent="amber"
      />
      <KpiCard
        icon={Activity}
        label="Fuente de datos"
        value={etiquetaFuenteAnalitica(analytics.source)}
        sublabel="Analítica operativa"
        accent="violet"
      />
    </div>
  );
}
