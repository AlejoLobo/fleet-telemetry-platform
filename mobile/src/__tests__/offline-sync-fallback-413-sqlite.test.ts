import { createSqliteMemoryDb, getSqliteRows, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";
import type { TelemetryApiErrorCategory } from "@/services/telemetry-api";

const mockMemoryDb = createSqliteMemoryDb();
const mockSendSingleEvent = jest.fn();
const mockSendBatchEvents = jest.fn();
const mockHandleUnauthorized = jest.fn();
const mockMarkForbidden = jest.fn();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

jest.mock("@/services/telemetry-api", () => ({
  sendSingleEvent: (...args: unknown[]) => mockSendSingleEvent(...args),
  sendBatchEvents: (...args: unknown[]) => mockSendBatchEvents(...args),
  TelemetryApiError: class TelemetryApiError extends Error {
    status: number;
    category: string;
    sanitizedMessage: string;
    constructor(status: number, category: string, message: string) {
      super(message);
      this.status = status;
      this.category = category;
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

import {
  enqueueEvent,
  getQueueEventByEventId,
  resetOfflineQueueForTests,
} from "@/db/offline-queue";
import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";
import { TelemetryApiError } from "@/services/telemetry-api";

const base = {
  vehicleId: "VH-001",
  driverId: "DRV-001",
  timestamp: "2026-07-10T10:00:00Z",
  latitude: 4.65,
  longitude: -74.08,
  speedKmh: 40,
  fuelLevelPercent: 70,
  batteryPercent: 90,
};

function apiError(status: number, category: TelemetryApiErrorCategory) {
  return new TelemetryApiError(status, category, `HTTP ${status}`);
}

async function seedEvents(ids: string[]) {
  for (const eventId of ids) {
    await enqueueEvent({ ...base, eventId });
  }
}

function assertNoProcessing() {
  getSqliteRows().forEach((row) => {
    expect(row.status).not.toBe("processing");
    expect(row.locked_at).toBeNull();
  });
}

describe("fallback 413 individual con SQLite real", () => {
  beforeEach(async () => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    resetSyncCoordinatorForTests();
    jest.clearAllMocks();
  });

  it("Batch_400_evento_individual_413_no_bloquea_validos", async () => {
    await seedEvents(["A", "B", "C"]);
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "A") throw apiError(413, "payload_too_large");
      if (payload.eventId === "B") return;
      if (payload.eventId === "C") return;
    });

    const result = await syncPendingQueue(true);

    expect(result.status).toBe("completed");
    expect(result.permanentFailures).toBe(1);
    expect(result.synced).toBe(2);
    expect((await getQueueEventByEventId("A"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("A"))?.lastError).toBe("Payload demasiado grande");
    expect((await getQueueEventByEventId("B"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("C"))?.status).toBe("synced");
    assertNoProcessing();
  });

  it("Batch_422_evento_individual_413_no_bloquea_validos", async () => {
    await seedEvents(["A", "B", "C"]);
    mockSendBatchEvents.mockRejectedValue(apiError(422, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "A") throw apiError(413, "payload_too_large");
      if (payload.eventId === "B") return;
      if (payload.eventId === "C") return;
    });

    const result = await syncPendingQueue(true);

    expect(result.status).toBe("completed");
    expect(result.permanentFailures).toBe(1);
    expect(result.synced).toBe(2);
    expect((await getQueueEventByEventId("A"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("B"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("C"))?.status).toBe("synced");
    assertNoProcessing();
  });

  it("Fallback_exito_413_401", async () => {
    await seedEvents(["A", "B", "C"]);
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "A") return;
      if (payload.eventId === "B") throw apiError(413, "payload_too_large");
      if (payload.eventId === "C") throw apiError(401, "auth_required");
    });

    const result = await syncPendingQueue(true);

    expect(result.status).toBe("auth_required");
    expect((await getQueueEventByEventId("A"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("B"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("B"))?.status).not.toBe("pending");
    expect((await getQueueEventByEventId("C"))?.status).toBe("pending");
    assertNoProcessing();
  });

  it("Fallback_413_500", async () => {
    await seedEvents(["A", "B"]);
    mockSendBatchEvents.mockRejectedValue(apiError(422, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "A") throw apiError(413, "payload_too_large");
      if (payload.eventId === "B") throw apiError(500, "transient");
    });

    const result = await syncPendingQueue(true);

    expect(result.status).toBe("deferred");
    expect((await getQueueEventByEventId("A"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("A"))?.status).not.toBe("retry");
    expect((await getQueueEventByEventId("B"))?.status).toBe("retry");
    assertNoProcessing();
  });

  it("Fallback_dos_eventos_413", async () => {
    await seedEvents(["BIG1", "BIG2", "OK"]);
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "BIG1") throw apiError(413, "payload_too_large");
      if (payload.eventId === "BIG2") throw apiError(413, "payload_too_large");
      if (payload.eventId === "OK") return;
    });

    const result = await syncPendingQueue(true);

    expect(result.permanentFailures).toBe(2);
    expect(result.synced).toBe(1);
    expect((await getQueueEventByEventId("BIG1"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("BIG2"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("OK"))?.status).toBe("synced");
    assertNoProcessing();
  });
});
