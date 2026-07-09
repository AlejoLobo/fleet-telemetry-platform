/** Panel lateral de alertas activas (versión embebida). */
"use client";

import { AlertTriangle, Bell, CheckCircle2, ShieldAlert } from "lucide-react";
import type { FleetAlert } from "@/types/fleet";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  esSeveridadCritica,
  etiquetaSeveridad,
  etiquetaTipoAlerta,
  traducirMensajeAlerta,
} from "@/lib/labels";
import { cn } from "@/lib/utils";

type AlertsPanelProps = {
  alerts: FleetAlert[];
  onAcknowledge?: (alertId: string) => Promise<void>;
  acknowledgingId?: string | null;
};

/** Lista de alertas con opción de confirmar. */
export function AlertsPanel({ alerts, onAcknowledge, acknowledgingId }: AlertsPanelProps) {
  const criticalCount = alerts.filter((a) => esSeveridadCritica(a.severity)).length;

  return (
    <Card className="flex h-full flex-col">
      <CardHeader className="border-b border-border">
        <div className="flex items-center justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Bell className="h-4 w-4 text-amber-500" />
              Alertas activas
            </CardTitle>
            <CardDescription className="mt-1">
              {criticalCount > 0
                ? `${criticalCount} crítica(s) · atención inmediata`
                : "Monitoreo de incidentes"}
            </CardDescription>
          </div>
          <Badge variant={criticalCount > 0 ? "critical" : "outline"} className="tabular-nums">
            {alerts.length}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="custom-scrollbar max-h-[420px] flex-1 overflow-y-auto pt-4">
        <div className="space-y-3">
          {alerts.length === 0 && (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-emerald-50">
                <ShieldAlert className="h-7 w-7 text-emerald-400" />
              </div>
              <p className="mt-3 text-sm font-medium text-slate-600">Todo en orden</p>
              <p className="text-xs text-muted-foreground">No hay alertas abiertas</p>
            </div>
          )}
          {alerts.map((alert, index) => {
            const critical = esSeveridadCritica(alert.severity);

            return (
              <div
                key={alert.alertId}
                className={cn(
                  "relative rounded-xl border p-4 transition-shadow hover:shadow-soft",
                  critical
                    ? "border-red-200 bg-gradient-to-r from-red-50 to-white"
                    : "border-amber-200 bg-gradient-to-r from-amber-50 to-white",
                )}
                style={{ animationDelay: `${index * 50}ms` }}
              >
                <div className="mb-2 flex flex-wrap items-center gap-2">
                  <Badge variant={critical ? "critical" : "warning"}>
                    {etiquetaSeveridad(alert.severity)}
                  </Badge>
                  <span className="font-semibold text-slate-800">{alert.vehicleId}</span>
                  <span className="text-xs text-slate-400">·</span>
                  <span className="text-xs font-medium text-slate-500">
                    {etiquetaTipoAlerta(alert.alertType)}
                  </span>
                </div>
                <p className="text-sm leading-relaxed text-slate-700">
                  {traducirMensajeAlerta(alert)}
                </p>
                <div className="mt-3 flex items-center justify-between gap-2">
                  <p className="flex items-center gap-1.5 text-xs text-slate-400">
                    <AlertTriangle className="h-3 w-3" />
                    {new Date(alert.createdAt).toLocaleString("es-CO")}
                  </p>
                  {onAcknowledge && (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      disabled={acknowledgingId === alert.alertId}
                      onClick={() => onAcknowledge(alert.alertId)}
                      className="h-7 gap-1 text-xs"
                    >
                      <CheckCircle2 className="h-3.5 w-3.5" />
                      {acknowledgingId === alert.alertId ? "..." : "Confirmar"}
                    </Button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </CardContent>
    </Card>
  );
}
