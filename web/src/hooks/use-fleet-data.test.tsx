/** @vitest-environment jsdom */
import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useFleetData } from "@/hooks/use-fleet-data";
import { ResyncFailedError, ResyncSupersededError } from "@/lib/sse-resync";
import { ApiError } from "@/lib/api-client";

const fetchFleetLive = vi.fn();
const fetchAlertsLive = vi.fn();
const fetchOpsSummary = vi.fn();
const fetchTelemetrySnapshot = vi.fn();

vi.mock("@/lib/api-client", () => {
  class ApiError extends Error {
    readonly status: number;
    readonly retryAfterSeconds?: number;
    constructor(message: string, status: number, retryAfterSeconds?: number) {
      super(message);
      this.name = "ApiError";
      this.status = status;
      this.retryAfterSeconds = retryAfterSeconds;
    }
  }

  return {
    ApiError,
    apiClient: {
      fetchFleetLive: (...args: unknown[]) => fetchFleetLive(...args),
      fetchAlertsLive: (...args: unknown[]) => fetchAlertsLive(...args),
      fetchOpsSummary: (...args: unknown[]) => fetchOpsSummary(...args),
    },
  };
});

vi.mock("@/lib/fleet-pagination", () => ({
  fetchTelemetrySnapshot: (...args: unknown[]) => fetchTelemetrySnapshot(...args),
}));

vi.mock("@/lib/utils", () => ({
  getApiBaseUrl: () => "http://localhost:5000",
}));

const vehicle = {
  deviceId: "00000000-0000-4000-8000-000000000001",
  vehicleName: "00000000-0000-4000-8000-000000000001",
  vehicleType: "car" as const,
  status: "online",
  lastSeenAt: "2026-07-10T10:00:00Z",
  lastSpeedKmh: 1,
  lastLatitude: 1,
  lastLongitude: 1,
};

describe("useFleetData truncated snapshot", () => {
  beforeEach(() => {
    fetchFleetLive.mockReset();
    fetchAlertsLive.mockReset();
    fetchOpsSummary.mockReset();
    fetchTelemetrySnapshot.mockReset();
    fetchTelemetrySnapshot.mockResolvedValue({
      events: [{ eventId: "e1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
      partial: false,
      truncated: false,
    });
  });

  it("useFleetData_muestra_snapshot_truncado", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: true,
      truncated: true,
    });
    fetchAlertsLive.mockResolvedValue([
      { alertId: "a1", deviceId: "00000000-0000-4000-8000-000000000001", alertType: "x", severity: "low", message: "", createdAt: "", isAcknowledged: false },
      { alertId: "a2", deviceId: "00000000-0000-4000-8000-000000000002", alertType: "x", severity: "low", message: "", createdAt: "", isAcknowledged: false },
    ]);
    fetchOpsSummary.mockResolvedValue({ totalVehicles: 9000, activeVehicles: 6000, criticalAlerts: 99 });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));

    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    expect(result.current.fleetTruncated).toBe(true);
    expect(result.current.fleetError).toContain("Snapshot parcial");
    expect(result.current.globalAnalytics.totalVehicles).toBe(9000);
    expect(result.current.globalAnalytics.activeVehicles).toBe(6000);
    expect(result.current.globalAnalytics.partial).toBe(true);
    expect(result.current.globalAnalytics.aggregationSource).toBe("ops");
    expect(result.current.globalAnalytics.openAlerts).toBe(2);
    expect(result.current.globalAnalytics.openAlerts).not.toBe(99);
  });

  it("Ops_failure_marca_metricas_como_snapshot_parcial", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle, { ...vehicle, deviceId: "00000000-0000-4000-8000-000000000002", vehicleName: "00000000-0000-4000-8000-000000000002" }],
      partial: true,
      truncated: true,
    });
    fetchAlertsLive.mockResolvedValue([
      { alertId: "a1", deviceId: "00000000-0000-4000-8000-000000000001", alertType: "x", severity: "critical", message: "", createdAt: "", isAcknowledged: false },
    ]);
    fetchOpsSummary.mockRejectedValue(new Error("ops unavailable"));

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));

    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    expect(result.current.globalAnalytics.totalVehicles).toBe(2);
    expect(result.current.globalAnalytics.activeVehicles).toBe(2);
    expect(result.current.globalAnalytics.openAlerts).toBe(1);
    expect(result.current.globalAnalytics.aggregationSource).toBe("snapshot");
    expect(result.current.globalAnalytics.partial).toBe(true);
  });
});

describe("useFleetData refreshForResync", () => {
  beforeEach(() => {
    fetchFleetLive.mockReset();
    fetchAlertsLive.mockReset();
    fetchOpsSummary.mockReset();
    fetchTelemetrySnapshot.mockReset();
  });

  it("refreshForResync_exitoso_carga_flota_alertas_y_telemetria", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({
      events: [{ eventId: "e1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
      partial: false,
      truncated: false,
    });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    await result.current.loadFromApi();
    await result.current.refreshForResync("00000000-0000-4000-8000-000000000001");

    expect(fetchFleetLive).toHaveBeenCalled();
    expect(fetchAlertsLive).toHaveBeenCalled();
    expect(fetchTelemetrySnapshot).toHaveBeenCalledWith("00000000-0000-4000-8000-000000000001", expect.anything());
    expect(result.current.vehicles).toHaveLength(1);
  });

  it("Fallo_real_de_fetchFleetLive_propaga_error_de_resync", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({ events: [], partial: false, truncated: false });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));
    await result.current.loadFromApi();

    fetchFleetLive.mockRejectedValue(new Error("fleet down"));
    await expect(result.current.refreshForResync("00000000-0000-4000-8000-000000000001")).rejects.toThrow("fleet down");
  });

  it("Fallo_real_de_fetchAlertsLive_propaga_error_de_resync", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({ events: [], partial: false, truncated: false });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));
    await result.current.loadFromApi();

    fetchAlertsLive.mockRejectedValue(new Error("alerts down"));
    await expect(result.current.refreshForResync("00000000-0000-4000-8000-000000000001")).rejects.toThrow("alerts down");
  });

  it("Fallo_real_de_telemetria_propaga_error_de_resync", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({ events: [], partial: false, truncated: false });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));
    await result.current.loadFromApi();

    fetchTelemetrySnapshot.mockResolvedValue({
      events: [],
      partial: true,
      error: "telemetry unavailable",
      truncated: false,
    });
    await expect(result.current.refreshForResync("00000000-0000-4000-8000-000000000001")).rejects.toThrow("telemetry unavailable");
  });

  it("refreshForResync_acepta_flota_vacia_como_snapshot_valido", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));
    await result.current.loadFromApi();

    fetchTelemetrySnapshot.mockClear();
    const snapshot = await result.current.refreshForResync("00000000-0000-4000-8000-000000000001");
    expect(snapshot.resolvedDeviceId).toBeNull();
    expect(result.current.vehicles).toHaveLength(0);
    expect(fetchTelemetrySnapshot).not.toHaveBeenCalled();
  });

  it("refreshForResync_elige_primer_vehiculo_si_el_seleccionado_no_existe", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle, { ...vehicle, deviceId: "00000000-0000-4000-8000-000000000002", vehicleName: "00000000-0000-4000-8000-000000000002" }],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({
      events: [],
      partial: false,
      truncated: false,
    });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000093"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));
    await result.current.loadFromApi();

    fetchTelemetrySnapshot.mockClear();
    const snapshot = await result.current.refreshForResync("00000000-0000-4000-8000-000000000093");
    expect(snapshot.resolvedDeviceId).toBe("00000000-0000-4000-8000-000000000001");
    expect(fetchTelemetrySnapshot).toHaveBeenLastCalledWith("00000000-0000-4000-8000-000000000001");
  });

  it("carga_normal_antigua_no_sobrescribe_snapshot_de_resync", async () => {
    let resolveFleet: ((value: unknown) => void) | undefined;
    const deferredFleet = new Promise((resolve) => {
      resolveFleet = resolve;
    });

    fetchFleetLive.mockImplementation(() => deferredFleet as Promise<never>);
    fetchAlertsLive.mockResolvedValue([]);

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(true));

    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({ events: [], partial: false, truncated: false });

    const resyncPromise = result.current.refreshForResync("00000000-0000-4000-8000-000000000001");
    await waitFor(() => expect(result.current.vehicles).toHaveLength(1));
    await resyncPromise;

    resolveFleet?.({
      vehicles: [{ ...vehicle, deviceId: "00000000-0000-4000-8000-000000000098", vehicleName: "00000000-0000-4000-8000-000000000098" }],
      partial: false,
      truncated: false,
    });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(result.current.vehicles[0]?.deviceId).toBe("00000000-0000-4000-8000-000000000001");
  });

  it("telemetria_antigua_no_sobrescribe_resync", async () => {
    let resolveStale: ((value: unknown) => void) | undefined;
    const staleDeferred = new Promise((resolve) => {
      resolveStale = resolve;
    });
    let telemetryCalls = 0;

    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockImplementation(async () => {
      telemetryCalls += 1;
      if (telemetryCalls === 1) {
        return staleDeferred;
      }
      return {
        events: [{ eventId: "fresh", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 1 }],
        partial: false,
        truncated: false,
      };
    });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    const resyncPromise = result.current.refreshForResync("00000000-0000-4000-8000-000000000001");
    await waitFor(() => expect(result.current.telemetry).toHaveLength(1));
    expect(result.current.telemetry[0]?.eventId).toBe("fresh");
    await resyncPromise;

    resolveStale?.({
      events: [{ eventId: "stale", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-09T10:00:00Z", latitude: 2, longitude: 2, speedKmh: 2 }],
      partial: false,
      truncated: false,
    });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(result.current.telemetry[0]?.eventId).toBe("fresh");
  });

  it("loadFromApi_obsoleto_no_inicia_telemetria_despues_de_resync", async () => {
    let resolveFleet: ((value: unknown) => void) | undefined;
    const deferredFleet = new Promise((resolve) => {
      resolveFleet = resolve;
    });
    let fleetCalls = 0;

    fetchFleetLive.mockImplementation(async () => {
      fleetCalls += 1;
      if (fleetCalls === 1) return deferredFleet;
      return { vehicles: [vehicle], partial: false, truncated: false };
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({ events: [], partial: false, truncated: false });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(true));

    fetchTelemetrySnapshot.mockClear();
    await act(async () => {
      await result.current.refreshForResync("00000000-0000-4000-8000-000000000001");
    });
    await waitFor(() => expect(result.current.vehicles).toHaveLength(1));

    const telemetryCallsBefore = fetchTelemetrySnapshot.mock.calls.length;
    resolveFleet?.({ vehicles: [{ ...vehicle, deviceId: "00000000-0000-4000-8000-000000000098" }], partial: false, truncated: false });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(fetchTelemetrySnapshot.mock.calls.length).toBe(telemetryCallsBefore);
    expect(result.current.vehicles[0]?.deviceId).toBe("00000000-0000-4000-8000-000000000001");
  });

  it("dos_resync_concurrentes_solo_el_ultimo_aplica_estado", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({ events: [], partial: false, truncated: false });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    let resolveFirstFleet: ((value: unknown) => void) | undefined;
    const firstFleet = new Promise((resolve) => {
      resolveFirstFleet = resolve;
    });
    let fleetCalls = 0;

    fetchFleetLive.mockImplementation(async () => {
      fleetCalls += 1;
      if (fleetCalls === 1) return firstFleet;
      return {
        vehicles: [{ ...vehicle, deviceId: "00000000-0000-4000-8000-000000000097", vehicleName: "00000000-0000-4000-8000-000000000097" }],
        partial: false,
        truncated: false,
      };
    });

    const first = result.current.refreshForResync("00000000-0000-4000-8000-000000000001");
    const second = result.current.refreshForResync("00000000-0000-4000-8000-000000000001");

    await expect(second).resolves.toMatchObject({
      resolvedDeviceId: "00000000-0000-4000-8000-000000000097",
      applied: true,
    });
    await waitFor(() => expect(result.current.vehicles[0]?.deviceId).toBe("00000000-0000-4000-8000-000000000097"));

    resolveFirstFleet?.({
      vehicles: [{ ...vehicle, deviceId: "00000000-0000-4000-8000-000000000096", vehicleName: "00000000-0000-4000-8000-000000000096" }],
      partial: false,
      truncated: false,
    });
    await expect(first).rejects.toThrow(/superseded/i);
    expect(result.current.vehicles[0]?.deviceId).toBe("00000000-0000-4000-8000-000000000097");
  });

  it("flota_vacia_limpia_telemetria_sin_solicitudes_posteriores", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    fetchTelemetrySnapshot.mockClear();
    await result.current.loadFromApi();

    expect(result.current.telemetry).toHaveLength(0);
    expect(result.current.selectedAnalytics).toBeNull();
    expect(fetchTelemetrySnapshot).not.toHaveBeenCalled();

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(fetchTelemetrySnapshot).not.toHaveBeenCalled();
  });

  it("no_consulta_telemetria_de_vehiculo_eliminado", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({ events: [], partial: false, truncated: false });

    const { result, rerender } = renderHook(
      ({ deviceId }: { deviceId: string | null }) => useFleetData(deviceId),
      { initialProps: { deviceId: "00000000-0000-4000-8000-000000000001" as string | null } },
    );
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    fetchFleetLive.mockResolvedValue({
      vehicles: [{ ...vehicle, deviceId: "00000000-0000-4000-8000-000000000002", vehicleName: "00000000-0000-4000-8000-000000000002" }],
      partial: false,
      truncated: false,
    });
    await result.current.loadFromApi();
    fetchTelemetrySnapshot.mockClear();

    rerender({ deviceId: "00000000-0000-4000-8000-000000000001" });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(fetchTelemetrySnapshot).not.toHaveBeenCalled();
    expect(result.current.telemetry).toHaveLength(0);
  });

  it("carga_inicial_con_ApiError_429_muestra_mensaje_especifico", async () => {
    fetchFleetLive.mockRejectedValue(new ApiError("Demasiadas solicitudes", 429, 60));
    fetchAlertsLive.mockResolvedValue([]);

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    expect(result.current.fleetError).toContain("limitó temporalmente");
    expect(result.current.fleetError).not.toContain("No se pudo conectar");
    expect(fetchTelemetrySnapshot).not.toHaveBeenCalled();
  });

  it("loadFromApi_no_consulta_telemetria_si_falla_la_flota", async () => {
    fetchFleetLive.mockRejectedValue(new ApiError("boom", 503));
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockClear();

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    await act(async () => {
      await result.current.loadFromApi();
    });

    expect(result.current.fleetError).toContain("503");
    expect(fetchTelemetrySnapshot).not.toHaveBeenCalled();
  });

  it("error_de_telemetria_conserva_historial_anterior", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({
      events: [{ eventId: "e1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
      partial: false,
      truncated: false,
    });

    const { result } = renderHook(() => useFleetData("00000000-0000-4000-8000-000000000001"));
    await waitFor(() => expect(result.current.telemetry.length).toBe(1));

    fetchTelemetrySnapshot.mockRejectedValueOnce(new ApiError("servicio caído", 503));
    await act(async () => {
      await result.current.loadFromApi();
    });
    await waitFor(() => expect(result.current.telemetryLoading).toBe(false));

    expect(result.current.telemetry).toHaveLength(1);
    expect(result.current.telemetryError).toContain("503");
    expect(result.current.telemetryError).not.toContain("Failed to fetch");
  });
});

describe("useFleetData refreshSelectedTelemetry", () => {
  beforeEach(() => {
    fetchFleetLive.mockReset();
    fetchAlertsLive.mockReset();
    fetchOpsSummary.mockReset();
    fetchTelemetrySnapshot.mockReset();
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({
      events: [{
        eventId: "e1",
        deviceId: "00000000-0000-4000-8000-000000000001",
        timestamp: "2026-07-10T10:00:00Z",
        latitude: 1,
        longitude: 1,
        speedKmh: 10,
      }],
      partial: false,
      truncated: false,
    });
  });

  it("refresca solo telemetría del seleccionado sin reload de flota", async () => {
    const { result } = renderHook(() =>
      useFleetData("00000000-0000-4000-8000-000000000001"),
    );
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));
    await waitFor(() => expect(result.current.telemetry.length).toBe(1));

    const fleetCalls = fetchFleetLive.mock.calls.length;
    const alertCalls = fetchAlertsLive.mock.calls.length;
    fetchTelemetrySnapshot.mockClear();

    await act(async () => {
      await result.current.refreshSelectedTelemetry();
    });

    expect(fetchTelemetrySnapshot).toHaveBeenCalledTimes(1);
    expect(fetchFleetLive.mock.calls.length).toBe(fleetCalls);
    expect(fetchAlertsLive.mock.calls.length).toBe(alertCalls);
  });

  it("cambiar de vehículo cancela solicitudes anteriores", async () => {
    let resolveFirst!: (value: unknown) => void;
    const first = new Promise((resolve) => {
      resolveFirst = resolve;
    });

    fetchTelemetrySnapshot.mockImplementationOnce(
      (_deviceId: string, options?: { signal?: AbortSignal }) =>
        first.then(() => {
          if (options?.signal?.aborted) {
            const err = new Error("aborted");
            err.name = "AbortError";
            throw err;
          }
          return {
            events: [{
              eventId: "old",
              deviceId: "00000000-0000-4000-8000-000000000001",
              timestamp: "2026-07-10T10:00:00Z",
              latitude: 1,
              longitude: 1,
              speedKmh: 1,
            }],
            partial: false,
            truncated: false,
          };
        }),
    );

    fetchFleetLive.mockResolvedValue({
      vehicles: [
        vehicle,
        {
          ...vehicle,
          deviceId: "00000000-0000-4000-8000-000000000002",
          vehicleName: "00000000-0000-4000-8000-000000000002",
        },
      ],
      partial: false,
      truncated: false,
    });

    const { result, rerender } = renderHook(
      ({ id }: { id: string | null }) => useFleetData(id),
      { initialProps: { id: "00000000-0000-4000-8000-000000000001" } },
    );

    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    fetchTelemetrySnapshot.mockResolvedValue({
      events: [{
        eventId: "new",
        deviceId: "00000000-0000-4000-8000-000000000002",
        timestamp: "2026-07-10T11:00:00Z",
        latitude: 2,
        longitude: 2,
        speedKmh: 20,
      }],
      partial: false,
      truncated: false,
    });

    rerender({ id: "00000000-0000-4000-8000-000000000002" });

    await act(async () => {
      resolveFirst({
        events: [],
        partial: false,
        truncated: false,
      });
      await Promise.resolve();
    });

    await waitFor(() =>
      expect(result.current.telemetry.some((e) => e.eventId === "new")).toBe(true),
    );
    expect(result.current.telemetry.some((e) => e.eventId === "old")).toBe(false);
  });

  it("no ejecuta sin dispositivo seleccionado", async () => {
    const { result } = renderHook(() => useFleetData(null));
    await waitFor(() => expect(result.current.fleetLoading).toBe(false));
    fetchTelemetrySnapshot.mockClear();

    await act(async () => {
      await result.current.refreshSelectedTelemetry();
    });

    expect(fetchTelemetrySnapshot).not.toHaveBeenCalled();
  });
});
