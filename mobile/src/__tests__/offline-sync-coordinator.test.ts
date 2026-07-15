import { TelemetryApiError, type TelemetryApiErrorCategory } from "@/services/telemetry-api";
import type { QueuedTelemetryEvent } from "@/types/telemetry";

const mockClaimNextBatch = jest.fn();
const mockCountPendingEvents = jest.fn();
const mockPurgeSyncedOlderThan = jest.fn();
const mockMarkEventsSynced = jest.fn();
const mockMarkEventPermanentFailure = jest.fn();
const mockMarkEventRetry = jest.fn();
const mockMarkClaimedBatchRetryAtomic = jest.fn();
const mockReleaseClaimedEvents = jest.fn();
const mockSendSingleEvent = jest.fn();
const mockSendBatchEvents = jest.fn();
const mockHandleUnauthorized = jest.fn();
const mockMarkForbidden = jest.fn();

jest.mock("@/db/offline-queue", () => ({
  claimNextBatch: (...args: unknown[]) => mockClaimNextBatch(...args),
  countPendingEvents: () => mockCountPendingEvents(),
  purgeSyncedOlderThan: (...args: unknown[]) => mockPurgeSyncedOlderThan(...args),
  markEventsSynced: (...args: unknown[]) => mockMarkEventsSynced(...args),
  markEventPermanentFailure: (...args: unknown[]) => mockMarkEventPermanentFailure(...args),
  markEventRetry: (...args: unknown[]) => mockMarkEventRetry(...args),
  markClaimedBatchRetryAtomic: (...args: unknown[]) => mockMarkClaimedBatchRetryAtomic(...args),
  releaseClaimedEvents: (...args: unknown[]) => mockReleaseClaimedEvents(...args),
  toPayload: (event: QueuedTelemetryEvent) => event,
}));

jest.mock("@/services/telemetry-api", () => ({
  sendSingleEvent: (...args: unknown[]) => mockSendSingleEvent(...args),
  sendBatchEvents: (...args: unknown[]) => mockSendBatchEvents(...args),
  TelemetryApiError: class TelemetryApiError extends Error {
    status: number;
    category: string;
    retryAfterSeconds?: number;
    sanitizedMessage: string;
    constructor(status: number, category: string, message: string, retryAfterSeconds?: number) {
      super(message);
      this.status = status;
      this.category = category;
      this.retryAfterSeconds = retryAfterSeconds;
      this.sanitizedMessage = message;
    }
  },
}));

jest.mock("@/services/auth-service", () => ({
  handleUnauthorizedFromApi: () => mockHandleUnauthorized(),
  markForbiddenFromApi: (...args: unknown[]) => mockMarkForbidden(...args),
}));

jest.mock("@/services/auth-runtime", () => ({
  getAuthRuntimeSnapshot: () => ({ mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false }),
}));


jest.mock("@/services/device-registry", () => ({
  ensureDeviceRegistered: jest.fn(async (deviceId: string) => ({
    deviceId,
    vehicleName: "VH-001",
  })),
  updateVehicleDisplayName: jest.fn(),
}));

import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";

function buildEvent(eventId: string, retryCount = 0): QueuedTelemetryEvent {
  return {
    localId: 1,
    eventId,
    deviceId: "11111111-1111-1111-1111-111111111111",
    driverId: "DRV-001",
    timestamp: "2026-07-10T10:00:00Z",
    latitude: 4.65,
    longitude: -74.08,
    speedKmh: 40,
    fuelLevelPercent: 70,
    batteryPercent: 90,
    source: "gps",
    status: "pending",
    retryCount,
    nextAttemptAt: null,
    lastAttemptAt: null,
    lastError: null,
    lockedAt: null,
    syncedAt: null,
    createdAt: "2026-07-10T09:00:00Z",
  };
}

function apiError(status: number, category: TelemetryApiErrorCategory, retryAfterSeconds?: number) {
  return new TelemetryApiError(status, category, `HTTP ${status}`, retryAfterSeconds);
}

describe("offline-sync-coordinator batch policy", () => {
  beforeEach(() => {
    resetSyncCoordinatorForTests();
    jest.clearAllMocks();
    mockClaimNextBatch.mockReset().mockResolvedValue([]);
    mockCountPendingEvents.mockReset().mockResolvedValue(0);
    mockPurgeSyncedOlderThan.mockReset().mockResolvedValue(0);
    mockMarkEventsSynced.mockReset();
    mockMarkEventPermanentFailure.mockReset();
    mockMarkEventRetry.mockReset();
    mockMarkClaimedBatchRetryAtomic.mockReset();
    mockReleaseClaimedEvents.mockReset();
    mockSendSingleEvent.mockReset();
    mockSendBatchEvents.mockReset();
    mockHandleUnauthorized.mockReset();
    mockMarkForbidden.mockReset();
  });

  it("Batch_2xx_marca_todos_synced", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockResolvedValue(undefined);
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.synced).toBe(2);
    expect(mockMarkEventsSynced).toHaveBeenCalledWith(["A", "B"]);
  });

  it("Batch_422_ejecuta_fallback_individual", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("BAD"), buildEvent("GOOD")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(422, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: QueuedTelemetryEvent) => {
      if (payload.eventId === "BAD") throw apiError(422, "validation");
    });
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.synced).toBe(1);
    expect(result.permanentFailures).toBe(1);
  });

  it("Un_evento_invalido_no_bloquea_los_validos", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("BAD"), buildEvent("GOOD"), buildEvent("ALSO")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: QueuedTelemetryEvent) => {
      if (payload.eventId === "BAD") throw apiError(400, "validation");
    });
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.synced).toBe(2);
    expect(result.permanentFailures).toBe(1);
  });

  it("Batch_401_no_ejecuta_singles_y_libera_todos", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(401, "auth_required"));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_required");
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
    expect(mockReleaseClaimedEvents).toHaveBeenCalledWith(["A", "B"], expect.any(String));
  });

  it("Batch_403_no_ejecuta_singles_y_libera_todos", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(403, "forbidden"));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("forbidden");
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
    expect(mockReleaseClaimedEvents).toHaveBeenCalled();
  });

  it("Batch_429_no_ejecuta_singles_y_respeta_Retry_After", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(429, "transient", 12));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("deferred");
    expect(mockMarkClaimedBatchRetryAtomic).toHaveBeenCalled();
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
  });

  it("Batch_500_no_ejecuta_singles_y_pasa_todos_a_retry", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(500, "transient"));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("deferred");
    expect(mockMarkClaimedBatchRetryAtomic).toHaveBeenCalled();
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
  });

  it("Error_404_conserva_eventos_y_devuelve_configuration_error", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(404, "protocol"));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("configuration_error");
    expect(mockReleaseClaimedEvents).toHaveBeenCalled();
    expect(mockMarkEventPermanentFailure).not.toHaveBeenCalled();
  });

  it("Timeout_no_ejecuta_singles", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(408, "timeout"));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("deferred");
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
    expect(mockMarkClaimedBatchRetryAtomic).toHaveBeenCalled();
  });

  it("Error_de_red_no_ejecuta_singles", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(0, "network"));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("deferred");
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
  });

  it("Batch_413_se_divide", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([
      buildEvent("A"),
      buildEvent("B"),
      buildEvent("C"),
      buildEvent("D"),
    ]).mockResolvedValueOnce([]);
    mockSendBatchEvents
      .mockRejectedValueOnce(apiError(413, "payload_too_large"))
      .mockResolvedValueOnce(undefined)
      .mockResolvedValueOnce(undefined);
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.synced).toBe(4);
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
    expect(mockSendBatchEvents).toHaveBeenCalledTimes(3);
  });

  it("Evento_individual_413_no_bloquea_el_resto", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("BIG"), buildEvent("SMALL")]).mockResolvedValueOnce([]);
    mockSendBatchEvents
      .mockRejectedValueOnce(apiError(413, "payload_too_large"))
      .mockRejectedValueOnce(apiError(413, "payload_too_large"))
      .mockResolvedValueOnce(undefined);
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.permanentFailures).toBe(1);
    expect(result.synced).toBe(1);
  });

  it("Error_inesperado_libera_todos_los_locks", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(new Error("unexpected programming error"));
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("failed");
    expect(mockReleaseClaimedEvents).toHaveBeenCalledWith(["A", "B"], expect.any(String));
    expect(mockSendSingleEvent).not.toHaveBeenCalled();
  });

  it("Ningun_camino_normal_deja_eventos_processing", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(500, "transient"));
    await syncPendingQueue(true, "test-device-id-001");
    expect(mockMarkClaimedBatchRetryAtomic).toHaveBeenCalled();
    expect(mockReleaseClaimedEvents).not.toHaveBeenCalled();
  });

  it("Durante_fallback_exito_luego_500_eventos_sin_procesar_quedan_desbloqueados", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B"), buildEvent("C")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: QueuedTelemetryEvent) => {
      if (payload.eventId === "A") return;
      if (payload.eventId === "B") throw apiError(500, "transient");
    });
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("deferred");
    expect(mockMarkEventsSynced).toHaveBeenCalledWith(["A"]);
    expect(mockMarkClaimedBatchRetryAtomic).toHaveBeenCalled();
  });

  it("Durante_fallback_exito_luego_401_eventos_sin_procesar_quedan_desbloqueados", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A"), buildEvent("B"), buildEvent("C")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: QueuedTelemetryEvent) => {
      if (payload.eventId === "A") return;
      if (payload.eventId === "B") throw apiError(401, "auth_required");
    });
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_required");
    expect(mockMarkEventsSynced).toHaveBeenCalledWith(["A"]);
    expect(mockReleaseClaimedEvents).toHaveBeenCalledWith(["B", "C"], expect.any(String));
    expect(mockHandleUnauthorized).toHaveBeenCalled();
  });

  it("Batch_400_ejecuta_fallback_individual", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("BAD"), buildEvent("GOOD")]).mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: QueuedTelemetryEvent) => {
      if (payload.eventId === "BAD") throw apiError(400, "validation");
    });
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.synced).toBe(1);
    expect(result.permanentFailures).toBe(1);
  });

  it("El_mutex_continua_evitando_dos_sincronizaciones_simultaneas", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("A")]).mockResolvedValueOnce([]);
    mockSendSingleEvent.mockImplementation(() => new Promise(() => undefined));
    syncPendingQueue(true, "test-device-id-001");
    syncPendingQueue(true, "test-device-id-001");
    // Registro de dispositivo + claim son microtareas encadenadas.
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    expect(mockClaimNextBatch).toHaveBeenCalledTimes(1);
    resetSyncCoordinatorForTests();
  });
});
