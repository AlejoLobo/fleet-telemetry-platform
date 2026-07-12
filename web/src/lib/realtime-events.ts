/** Contrato canónico de eventos SSE realtime. */
export const REALTIME_EVENTS = {
  connected: "connected",
  vehicleUpdate: "vehicle-update",
  fleetUpdate: "fleet-update",
  alert: "alert",
  heartbeat: "heartbeat",
  streamReset: "stream-reset",
} as const;
