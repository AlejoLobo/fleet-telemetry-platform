import type { VehicleStatus } from "@/types/fleet";

/** Umbral online del portal; debe alinearse con QueryLimits del backend. */
export function getOnlineThresholdMs(): number {
  const secondsRaw = process.env.NEXT_PUBLIC_ONLINE_THRESHOLD_SECONDS;
  if (secondsRaw) {
    const seconds = Number(secondsRaw);
    if (Number.isFinite(seconds) && seconds > 0) return seconds * 1000;
  }

  const minutesRaw = process.env.NEXT_PUBLIC_ONLINE_THRESHOLD_MINUTES;
  const minutes = minutesRaw ? Number(minutesRaw) : 1;
  if (Number.isFinite(minutes) && minutes > 0) return minutes * 60_000;
  return 45_000;
}

/** Recalcula online/offline a partir de lastSeenAt para no esperar solo al SSE. */
export function applyConnectivityFreshness(
  vehicles: VehicleStatus[],
  nowMs: number,
  onlineThresholdMs: number = getOnlineThresholdMs(),
): VehicleStatus[] {
  return vehicles.map((vehicle) => {
    if (!vehicle.lastSeenAt) {
      return vehicle.status === "offline" ? vehicle : { ...vehicle, status: "offline" };
    }

    const lastSeenMs = Date.parse(vehicle.lastSeenAt);
    if (Number.isNaN(lastSeenMs)) return vehicle;

    const nextStatus = nowMs - lastSeenMs <= onlineThresholdMs ? "online" : "offline";
    if (nextStatus === vehicle.status) return vehicle;
    return { ...vehicle, status: nextStatus };
  });
}
