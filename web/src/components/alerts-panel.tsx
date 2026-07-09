import type { FleetAlert } from "@/types/fleet";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";

export function AlertsPanel({ alerts }: { alerts: FleetAlert[] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Alertas abiertas ({alerts.length})</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="max-h-72 space-y-2 overflow-y-auto">
          {alerts.length === 0 && (
            <p className="text-sm text-muted-foreground">No hay alertas abiertas.</p>
          )}
          {alerts.map((alert) => (
            <div key={alert.alertId} className="rounded-md border border-border p-3 text-sm">
              <div className="mb-1 flex items-center gap-2">
                <Badge variant={alert.severity === "critical" ? "critical" : "warning"}>
                  {alert.severity}
                </Badge>
                <span className="font-medium">{alert.vehicleId}</span>
                <span className="text-muted-foreground">· {alert.alertType}</span>
              </div>
              <p>{alert.message}</p>
              <p className="mt-1 text-xs text-muted-foreground">
                {new Date(alert.createdAt).toLocaleString()}
              </p>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}
