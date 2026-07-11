import type { FleetAlert } from "@/types/fleet";

/** Elimina alertas duplicadas preservando el orden (primera ocurrencia gana). */
export function dedupeAlerts(alerts: FleetAlert[]): FleetAlert[] {
  const seen = new Set<string>();
  return alerts.filter((alert) => {
    if (seen.has(alert.alertId)) return false;
    seen.add(alert.alertId);
    return true;
  });
}
