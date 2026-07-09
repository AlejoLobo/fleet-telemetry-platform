import type { ElementType } from "react";
import { Activity, AlertTriangle, Gauge, Truck } from "lucide-react";
import type { AnalyticsSummary } from "@/types/fleet";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { etiquetaFuenteAnalitica } from "@/lib/labels";

function StatCard({
  icon: Icon,
  label,
  value,
  accent,
}: {
  icon: ElementType;
  label: string;
  value: string;
  accent: string;
}) {
  return (
    <div className={`rounded-xl border border-white/60 bg-white/80 p-4 shadow-sm ${accent}`}>
      <div className="mb-2 flex items-center gap-2 text-slate-500">
        <Icon className="h-4 w-4" />
        <span className="text-xs font-medium">{label}</span>
      </div>
      <p className="text-2xl font-bold text-slate-800">{value}</p>
    </div>
  );
}

export function AnalyticsSummaryPanel({ analytics }: { analytics: AnalyticsSummary }) {
  return (
    <Card className="h-full border-violet-100/80 shadow-sm">
      <CardHeader className="border-b border-violet-50 bg-gradient-to-r from-violet-50/80 to-sky-50/50 pb-4">
        <CardTitle className="text-slate-800">Resumen operativo</CardTitle>
      </CardHeader>
      <CardContent className="grid gap-3 pt-4">
        <StatCard
          icon={Gauge}
          label="Velocidad promedio"
          value={`${analytics.averageSpeedKmh} km/h`}
          accent="ring-1 ring-sky-100"
        />
        <StatCard
          icon={Truck}
          label="Vehículos en línea"
          value={`${analytics.activeVehicles}/${analytics.totalVehicles}`}
          accent="ring-1 ring-emerald-100"
        />
        <StatCard
          icon={AlertTriangle}
          label="Alertas abiertas"
          value={String(analytics.openAlerts)}
          accent="ring-1 ring-amber-100"
        />
        <div className="flex items-center gap-2 rounded-xl border border-slate-100 bg-slate-50/80 px-3 py-2 text-xs text-slate-500">
          <Activity className="h-3.5 w-3.5" />
          Fuente: {etiquetaFuenteAnalitica(analytics.source)}
        </div>
      </CardContent>
    </Card>
  );
}
