/** Modal y botón para ver y confirmar alertas activas. */
"use client";

import { useEffect } from "react";
import { AlertTriangle, Bell, CheckCircle2, ShieldAlert, X } from "lucide-react";
import type { FleetAlert } from "@/types/fleet";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  esSeveridadCritica,
  etiquetaSeveridad,
  etiquetaTipoAlerta,
  traducirMensajeAlerta,
} from "@/lib/labels";
import { cn } from "@/lib/utils";

type AlertsModalProps = {
  open: boolean;
  alerts: FleetAlert[];
  onClose: () => void;
  onAcknowledge?: (alertId: string) => Promise<void>;
  acknowledgingId?: string | null;
};

/** Botón del header que abre el modal de alertas. */
export function AlertsModalTrigger({
  alertCount,
  criticalCount,
  attention = false,
  onClick,
}: {
  alertCount: number;
  criticalCount: number;
  attention?: boolean;
  onClick: () => void;
}) {
  const hasAlerts = alertCount > 0;
  const isCritical = criticalCount > 0;

  return (
    <Button
      type="button"
      variant={isCritical ? "default" : "outline"}
      size="sm"
      onClick={onClick}
      className={cn(
        "relative gap-2 overflow-visible",
        isCritical && "bg-red-600 hover:bg-red-700",
        attention && "animate-alert-attention ring-2 ring-amber-400 ring-offset-2",
        attention && isCritical && "ring-red-400",
      )}
      aria-label={`Ver ${alertCount} alertas activas${attention ? " (nuevas alertas)" : ""}`}
    >
      {attention && (
        <span
          aria-hidden
          className={cn(
            "pointer-events-none absolute -inset-1 rounded-lg opacity-75",
            isCritical ? "animate-alert-ping bg-red-400/40" : "animate-alert-ping bg-amber-400/40",
          )}
        />
      )}
      <Bell className={cn("relative h-4 w-4", attention && "animate-bell-shake")} />
      Alertas
      {hasAlerts && (
        <span
          className={cn(
            "relative inline-flex min-w-[1.25rem] items-center justify-center rounded-full px-1.5 py-0.5 text-[10px] font-bold tabular-nums",
            isCritical ? "bg-white/20 text-white" : "bg-primary/10 text-primary",
            attention && "animate-pulse-soft",
          )}
        >
          {alertCount}
        </span>
      )}
    </Button>
  );
}

/** Modal con lista de alertas y confirmación. */
export function AlertsModal({
  open,
  alerts,
  onClose,
  onAcknowledge,
  acknowledgingId,
}: AlertsModalProps) {
  const criticalCount = alerts.filter((a) => esSeveridadCritica(a.severity)).length;

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", onKeyDown);
    return () => {
      document.body.style.overflow = "";
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-[2000] flex items-start justify-center p-4 pt-[max(1rem,10vh)] sm:p-6">
      <button
        type="button"
        className="absolute inset-0 bg-slate-900/50 backdrop-blur-sm"
        aria-label="Cerrar alertas"
        onClick={onClose}
      />

      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="alerts-modal-title"
        className="relative z-10 flex max-h-[min(80vh,720px)] w-full max-w-lg flex-col overflow-hidden rounded-2xl border border-border bg-white shadow-2xl animate-fade-up"
      >
        <header className="flex items-start justify-between gap-3 border-b border-border bg-gradient-to-r from-amber-50 via-white to-red-50 px-5 py-4">
          <div>
            <h2 id="alerts-modal-title" className="flex items-center gap-2 text-lg font-bold text-slate-900">
              <Bell className="h-5 w-5 text-amber-500" />
              Alertas activas
            </h2>
            <p className="mt-1 text-sm text-slate-500">
              {criticalCount > 0
                ? `${criticalCount} crítica(s) requieren atención`
                : `${alerts.length} alerta(s) abiertas`}
            </p>
          </div>
          <Button type="button" variant="outline" size="sm" onClick={onClose} className="shrink-0">
            <X className="h-4 w-4" />
          </Button>
        </header>

        <div className="custom-scrollbar flex-1 overflow-y-auto p-4">
          {alerts.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-emerald-50">
                <ShieldAlert className="h-8 w-8 text-emerald-400" />
              </div>
              <p className="mt-4 text-sm font-medium text-slate-600">Todo en orden</p>
              <p className="text-xs text-muted-foreground">No hay alertas abiertas</p>
            </div>
          ) : (
            <div className="space-y-3">
              {alerts.map((alert) => {
                const critical = esSeveridadCritica(alert.severity);
                return (
                  <article
                    key={alert.alertId}
                    className={cn(
                      "rounded-xl border p-4 transition-shadow hover:shadow-soft",
                      critical
                        ? "border-red-200 bg-gradient-to-r from-red-50 to-white"
                        : "border-amber-200 bg-gradient-to-r from-amber-50 to-white",
                    )}
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
                  </article>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
