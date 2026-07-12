/** Contrato canónico de eventos SSE realtime. */
export const REALTIME_EVENTS = {
  vehicleUpdate: "vehicle-update",
  fleetUpdate: "fleet-update",
  alert: "alert",
  heartbeat: "heartbeat",
  connected: "connected",
} as const;
