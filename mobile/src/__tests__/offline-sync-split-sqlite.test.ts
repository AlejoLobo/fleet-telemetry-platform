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

import { TelemetryApiError } from "@/services/telemetry-api";
import {
  enqueueEvent,
  getQueueEventByEventId,
  resetOfflineQueueForTests,
} from "@/db/offline-queue";
import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";

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

function batchKey(eventIds: string[]): string {
  return [...eventIds].sort().join(",");
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

describe("split 413 con SQLite real", () => {
  beforeEach(async () => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    resetSyncCoordinatorForTests();
    jest.clearAllMocks();
    mockSendSingleEvent.mockResolvedValue(undefined);
  });

  it("Split_413_primera_mitad_401_libera_todo", async () => {
    await seedEvents(["A", "B", "C", "D"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "A,B,C,D") throw apiError(413, "payload_too_large");
      if (key === "A,B") throw apiError(401, "auth_required");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("auth_required");
    expect((await getQueueEventByEventId("A"))?.status).toBe("pending");
    expect((await getQueueEventByEventId("B"))?.status).toBe("pending");
    expect((await getQueueEventByEventId("C"))?.status).toBe("pending");
    expect((await getQueueEventByEventId("D"))?.status).toBe("pending");
    assertNoProcessing();
  });

  it("Split_413_primera_mitad_500_reintenta_todo", async () => {
    await seedEvents(["A", "B", "C", "D"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "A,B,C,D") throw apiError(413, "payload_too_large");
      if (key === "A,B") throw apiError(500, "transient");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("deferred");
    for (const id of ["A", "B", "C", "D"]) {
      const row = await getQueueEventByEventId(id);
      expect(row?.status).toBe("retry");
      expect(row?.retryCount).toBe(1);
    }
    assertNoProcessing();
  });

  it("Split_413_primera_mitad_404_libera_todo", async () => {
    await seedEvents(["A", "B", "C", "D"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "A,B,C,D") throw apiError(413, "payload_too_large");
      if (key === "A,B") throw apiError(404, "protocol");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("configuration_error");
    for (const id of ["A", "B", "C", "D"]) {
      expect((await getQueueEventByEventId(id))?.status).toBe("pending");
    }
    assertNoProcessing();
  });

  it("Split_413_primera_mitad_403_libera_todo", async () => {
    await seedEvents(["A", "B", "C", "D"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "A,B,C,D") throw apiError(413, "payload_too_large");
      if (key === "A,B") throw apiError(403, "forbidden");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("forbidden");
    for (const id of ["A", "B", "C", "D"]) {
      expect((await getQueueEventByEventId(id))?.status).toBe("pending");
    }
    assertNoProcessing();
  });

  it("Split_413_error_inesperado_libera_todo", async () => {
    await seedEvents(["A", "B", "C", "D"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "A,B,C,D") throw apiError(413, "payload_too_large");
      if (key === "A,B") throw new Error("unexpected");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("failed");
    for (const id of ["A", "B", "C", "D"]) {
      expect((await getQueueEventByEventId(id))?.status).toBe("pending");
    }
    assertNoProcessing();
  });

  it("Split_413_recursivo_error_401_no_deja_hermanos_processing", async () => {
    await seedEvents(["E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "E1,E2,E3,E4,E5,E6,E7,E8") throw apiError(413, "payload_too_large");
      if (key === "E1,E2,E3,E4") throw apiError(413, "payload_too_large");
      if (key === "E1,E2") throw apiError(401, "auth_required");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("auth_required");
    for (let i = 1; i <= 8; i += 1) {
      expect((await getQueueEventByEventId(`E${i}`))?.status).toBe("pending");
    }
    assertNoProcessing();
  });

  it("Split_413_recursivo_error_500_no_deja_hermanos_processing", async () => {
    await seedEvents(["E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "E1,E2,E3,E4,E5,E6,E7,E8") throw apiError(413, "payload_too_large");
      if (key === "E1,E2,E3,E4") throw apiError(413, "payload_too_large");
      if (key === "E1,E2") throw apiError(500, "transient");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("deferred");
    for (let i = 1; i <= 8; i += 1) {
      expect((await getQueueEventByEventId(`E${i}`))?.status).toBe("retry");
    }
    assertNoProcessing();
  });

  it("Split_413_primera_mitad_exitosa_segunda_500", async () => {
    await seedEvents(["A", "B", "C", "D"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "A,B,C,D") throw apiError(413, "payload_too_large");
      if (key === "C,D") throw apiError(500, "transient");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("deferred");
    expect((await getQueueEventByEventId("A"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("B"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("C"))?.status).toBe("retry");
    expect((await getQueueEventByEventId("D"))?.status).toBe("retry");
    assertNoProcessing();
  });

  it("Split_413_evento_individual_demasiado_grande_y_rama_posterior_401", async () => {
    await seedEvents(["BIG", "B", "C", "D"]);
    mockSendBatchEvents.mockImplementation(async (events: { eventId: string }[]) => {
      const key = batchKey(events.map((e) => e.eventId));
      if (key === "B,BIG,C,D") throw apiError(413, "payload_too_large");
      if (key === "B,BIG") throw apiError(413, "payload_too_large");
      if (key === "BIG") throw apiError(413, "payload_too_large");
      if (key === "C,D") throw apiError(401, "auth_required");
    });

    const result = await syncPendingQueue(true);
    expect(result.status).toBe("auth_required");
    expect((await getQueueEventByEventId("BIG"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("B"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("C"))?.status).toBe("pending");
    expect((await getQueueEventByEventId("D"))?.status).toBe("pending");
    assertNoProcessing();
  });
});
