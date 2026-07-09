import type { AnalyticsSummary } from "@/types/fleet";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function AnalyticsSummaryPanel({ analytics }: { analytics: AnalyticsSummary }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Resumen analítico</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-2 gap-4 text-sm">
          <div>
            <dt className="text-muted-foreground">Velocidad promedio</dt>
            <dd className="text-2xl font-semibold">{analytics.averageSpeedKmh} km/h</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">Vehículos online</dt>
            <dd className="text-2xl font-semibold">
              {analytics.activeVehicles}/{analytics.totalVehicles}
            </dd>
          </div>
          <div>
            <dt className="text-muted-foreground">Alertas abiertas</dt>
            <dd className="text-2xl font-semibold">{analytics.openAlerts}</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">Fuente</dt>
            <dd className="text-xs">{analytics.source}</dd>
          </div>
        </dl>
      </CardContent>
    </Card>
  );
}
