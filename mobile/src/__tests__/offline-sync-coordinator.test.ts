import { TelemetryApiError } from "@/services/telemetry-api";
import type { QueuedTelemetryEvent } from "@/types/telemetry";

const mockClaimNextBatch = jest.fn();
const mockCountPendingEvents = jest.fn();
const mockPurgeSyncedOlderThan = jest.fn();
const mockMarkEventsSynced = jest.fn();
const mockMarkEventPermanentFailure = jest.fn();
const mockMarkEventRetry = jest.fn();
const mockSendSingleEvent = jest.fn();
const mockSendBatchEvents = jest.fn();

jest.mock("@/db/offline-queue", () => ({
  claimNextBatch: (...args: unknown[]) => mockClaimNextBatch(...args),
  countPendingEvents: () => mockCountPendingEvents(),
  purgeSyncedOlderThan: (...args: unknown[]) => mockPurgeSyncedOlderThan(...args),
  markEventsSynced: (...args: unknown[]) => mockMarkEventsSynced(...args),
  markEventPermanentFailure: (...args: unknown[]) => mockMarkEventPermanentFailure(...args),
  markEventRetry: (...args: unknown[]) => mockMarkEventRetry(...args),
  toPayload: (event: QueuedTelemetryEvent) => event,
}));

jest.mock("@/services/telemetry-api", () => ({
  sendSingleEvent: (...args: unknown[]) => mockSendSingleEvent(...args),
  sendBatchEvents: (...args: unknown[]) => mockSendBatchEvents(...args),
  TelemetryApiError: class TelemetryApiError extends Error {
    status: number;
    retryAfterSeconds?: number;
    constructor(status: number, message: string, retryAfterSeconds?: number) {
      super(message);
      this.status = status;
      this.retryAfterSeconds = retryAfterSeconds;
    }
  },
}));

import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";

function buildEvent(eventId: string, retryCount = 0): QueuedTelemetryEvent {
  return {
    localId: 1,
    eventId,
    vehicleId: "VH-001",
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

describe("offline-sync-coordinator", () => {
  beforeEach(() => {
    resetSyncCoordinatorForTests();
    jest.clearAllMocks();
    mockClaimNextBatch.mockReset().mockResolvedValue([]);
    mockCountPendingEvents.mockReset().mockResolvedValue(0);
    mockPurgeSyncedOlderThan.mockReset().mockResolvedValue(0);
    mockMarkEventsSynced.mockReset();
    mockMarkEventPermanentFailure.mockReset();
    mockMarkEventRetry.mockReset();
    mockSendSingleEvent.mockReset();
    mockSendBatchEvents.mockReset();
  });

  afterEach(() => {
    resetSyncCoordinatorForTests();
  });

  it("no sincroniza cuando no hay red", async () => {
    mockCountPendingEvents.mockResolvedValue(3);

    const result = await syncPendingQueue(false);

    expect(result.synced).toBe(0);
    expect(result.remaining).toBe(3);
    expect(mockClaimNextBatch).not.toHaveBeenCalled();
  });

  it("serializa sincronizaciones concurrentes con el mismo mutex", async () => {
    mockClaimNextBatch
      .mockResolvedValueOnce([buildEvent("E1")])
      .mockResolvedValueOnce([]);
    mockSendSingleEvent.mockImplementation(() => new Promise(() => undefined));

    syncPendingQueue(true);
    syncPendingQueue(true);
    await Promise.resolve();

    expect(mockClaimNextBatch).toHaveBeenCalledTimes(1);
    resetSyncCoordinatorForTests();
  });

  it("marca 400 como fallo permanente", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("BAD-400")]).mockResolvedValueOnce([]);
    mockSendSingleEvent.mockRejectedValue(new TelemetryApiError(400, "invalid"));

    const result = await syncPendingQueue(true);

    expect(result.permanentFailures).toBe(1);
    expect(mockMarkEventPermanentFailure).toHaveBeenCalledWith("BAD-400", expect.any(String));
    expect(mockMarkEventsSynced).not.toHaveBeenCalled();
  });

  it("reintenta error 500 transitorio", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("RETRY-500")]).mockResolvedValueOnce([]);
    mockSendSingleEvent.mockRejectedValue(new TelemetryApiError(500, "server"));

    const result = await syncPendingQueue(true);

    expect(result.retried).toBe(1);
    expect(mockMarkEventRetry).toHaveBeenCalledWith(
      "RETRY-500",
      1,
      expect.any(String),
      expect.any(String),
    );
  });

  it("respeta Retry-After en error 429", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("RATE-429")]).mockResolvedValueOnce([]);
    mockSendSingleEvent.mockRejectedValue(new TelemetryApiError(429, "rate limit", 20));

    await syncPendingQueue(true);

    const nextAttemptAt = mockMarkEventRetry.mock.calls[0][2] as string;
    const delayMs = new Date(nextAttemptAt).getTime() - Date.now();
    expect(delayMs).toBeGreaterThanOrEqual(19_000);
    expect(delayMs).toBeLessThanOrEqual(21_000);
  });

  it("continúa con eventos válidos tras fallo en lote", async () => {
    mockClaimNextBatch
      .mockResolvedValueOnce([buildEvent("BAD"), buildEvent("GOOD")])
      .mockResolvedValueOnce([]);
    mockSendBatchEvents.mockRejectedValue(new Error("batch failed"));
    mockSendSingleEvent.mockImplementation(async (payload: QueuedTelemetryEvent) => {
      if (payload.eventId === "BAD") throw new TelemetryApiError(400, "invalid");
    });

    const result = await syncPendingQueue(true);

    expect(result.synced).toBe(1);
    expect(result.permanentFailures).toBe(1);
    expect(mockMarkEventsSynced).toHaveBeenCalledWith(["GOOD"]);
  });

  it("no reenvía eventos ya sincronizados en la misma corrida", async () => {
    mockClaimNextBatch.mockResolvedValueOnce([buildEvent("DONE")]).mockResolvedValueOnce([]);
    mockSendSingleEvent.mockResolvedValue(undefined);

    await syncPendingQueue(true);

    expect(mockMarkEventsSynced).toHaveBeenCalledTimes(1);
    expect(mockSendSingleEvent).toHaveBeenCalledTimes(1);
  });
});
