import type { CursorPage } from "@/types/pagination";
import type { TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { normalizeVehicles } from "@/lib/fleet-normalize";
import { getApiBaseUrl } from "@/lib/utils";
import { ApiError } from "@/lib/api-client";

type FleetPageParams = {
  pageSize?: number;
  cursor?: string | null;
  liveOnly?: boolean;
  excludeSimulated?: boolean;
  signal?: AbortSignal;
};

type FleetSnapshotOptions = {
  maxVehicles?: number;
  pageSize?: number;
  liveOnly?: boolean;
  excludeSimulated?: boolean;
  signal?: AbortSignal;
};

type TelemetryPageParams = {
  vehicleId: string;
  from?: string;
  to?: string;
  pageSize?: number;
  cursor?: string | null;
  signal?: AbortSignal;
};

const DEFAULT_MAX_VEHICLES = 5000;
const DEFAULT_FLEET_PAGE_SIZE = 100;

function authHeaders(): Record<string, string> {
  if (typeof window === "undefined") return {};
  const token = localStorage.getItem("fleet_api_token");
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function fetchJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...authHeaders(),
      ...init?.headers,
    },
  });

  if (!response.ok) {
    let detail = `Error ${response.status} en ${path}`;
    try {
      const body = (await response.json()) as { detail?: string; title?: string };
      if (body.detail) detail = body.detail;
      else if (body.title) detail = body.title;
    } catch {
      // respuesta no JSON
    }
    throw new ApiError(detail, response.status);
  }

  return response.json() as Promise<T>;
}

function buildFleetQuery(params: FleetPageParams): string {
  const search = new URLSearchParams();
  if (params.pageSize) search.set("pageSize", String(params.pageSize));
  if (params.cursor) search.set("cursor", params.cursor);
  if (params.liveOnly) search.set("liveOnly", "true");
  if (params.excludeSimulated === false) search.set("excludeSimulated", "false");
  const query = search.toString();
  return query ? `/api/fleet?${query}` : "/api/fleet";
}

export async function fetchFleetPage(params: FleetPageParams = {}): Promise<CursorPage<VehicleStatus>> {
  const page = await fetchJson<CursorPage<Record<string, unknown>>>(buildFleetQuery(params), {
    signal: params.signal,
  });
  return {
    items: normalizeVehicles(page.items as Parameters<typeof normalizeVehicles>[0]),
    nextCursor: page.nextCursor,
    hasMore: page.hasMore,
  };
}

export async function fetchFleetSnapshot(options: FleetSnapshotOptions = {}): Promise<{
  vehicles: VehicleStatus[];
  partial: boolean;
  error?: string;
}> {
  const maxVehicles = options.maxVehicles ?? DEFAULT_MAX_VEHICLES;
  const pageSize = options.pageSize ?? DEFAULT_FLEET_PAGE_SIZE;
  const vehicles: VehicleStatus[] = [];
  const seenIds = new Set<string>();
  const seenCursors = new Set<string | null>();
  let cursor: string | null = null;
  let partial = false;
  let lastError: string | undefined;

  while (vehicles.length < maxVehicles) {
    if (options.signal?.aborted) {
      partial = true;
      break;
    }

    if (seenCursors.has(cursor)) {
      partial = true;
      lastError = "Cursor repetido detectado";
      break;
    }
    seenCursors.add(cursor);

    try {
      const page = await fetchFleetPage({
        pageSize,
        cursor,
        liveOnly: options.liveOnly,
        excludeSimulated: options.excludeSimulated,
        signal: options.signal,
      });

      for (const vehicle of page.items) {
        if (seenIds.has(vehicle.vehicleId)) continue;
        seenIds.add(vehicle.vehicleId);
        vehicles.push(vehicle);
        if (vehicles.length >= maxVehicles) break;
      }

      if (!page.hasMore || !page.nextCursor) break;
      cursor = page.nextCursor;
    } catch (error) {
      partial = true;
      lastError = error instanceof Error ? error.message : "Error al cargar flota";
      break;
    }
  }

  return { vehicles, partial, error: lastError };
}

export async function fetchTelemetryPage(params: TelemetryPageParams): Promise<CursorPage<TelemetryEvent>> {
  const search = new URLSearchParams();
  if (params.from) search.set("from", params.from);
  if (params.to) search.set("to", params.to);
  if (params.pageSize) search.set("pageSize", String(params.pageSize));
  if (params.cursor) search.set("cursor", params.cursor);
  const query = search.toString();
  const path = `/api/telemetry/${encodeURIComponent(params.vehicleId)}${query ? `?${query}` : ""}`;
  return fetchJson<CursorPage<TelemetryEvent>>(path, { signal: params.signal });
}

export async function fetchTelemetrySnapshot(
  vehicleId: string,
  options: { pageSize?: number; signal?: AbortSignal } = {},
): Promise<TelemetryEvent[]> {
  const pageSize = options.pageSize ?? 200;
  const events: TelemetryEvent[] = [];
  let cursor: string | null = null;
  const seenCursors = new Set<string | null>();

  while (true) {
    if (options.signal?.aborted) break;
    if (seenCursors.has(cursor)) break;
    seenCursors.add(cursor);

    const page = await fetchTelemetryPage({ vehicleId, pageSize, cursor, signal: options.signal });
    events.push(...page.items);
    if (!page.hasMore || !page.nextCursor) break;
    cursor = page.nextCursor;
  }

  return events;
}
