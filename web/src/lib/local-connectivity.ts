/** Reevaluación local online/offline a partir de lastSeenAt (sin HTTP). */
import type { VehicleStatus } from "@/types/fleet";

const DEFAULT_ONLINE_THRESHOLD_SECONDS = 300;

export function getOnlineThresholdSeconds(): number {
  const raw = Number(process.env.NEXT_PUBLIC_ONLINE_THRESHOLD_SECONDS ?? DEFAULT_ONLINE_THRESHOLD_SECONDS);
  return Number.isFinite(raw) && raw > 0 ? raw : DEFAULT_ONLINE_THRESHOLD_SECONDS;
}

export function resolveLocalConnectivityStatus(
  lastSeenAt: string | null | undefined,
  nowMs: number,
  thresholdSeconds = getOnlineThresholdSeconds(),
): "online" | "offline" {
  if (!lastSeenAt) return "offline";
  const seenMs = Date.parse(lastSeenAt);
  if (Number.isNaN(seenMs)) return "offline";
  return nowMs - seenMs <= thresholdSeconds * 1000 ? "online" : "offline";
}

export function applyLocalConnectivity(
  vehicles: VehicleStatus[],
  nowMs: number,
): VehicleStatus[] {
  return vehicles.map((vehicle) => ({
    ...vehicle,
    status: resolveLocalConnectivityStatus(vehicle.lastSeenAt, nowMs),
  }));
}
