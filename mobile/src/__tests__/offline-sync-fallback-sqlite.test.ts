import { createSqliteMemoryDb, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";
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

jest.mock("@/services/device-registry", () => ({
  ensureDeviceRegistered: jest.fn(async (deviceId: string) => ({
    deviceId,
    vehicleName: "VH-001",
  })),
  updateVehicleDisplayName: jest.fn(),
}));

import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";
import { TelemetryApiError } from "@/services/telemetry-api";

const base = {
  deviceId: "11111111-1111-1111-1111-111111111111",
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

describe("fallback parcial con SQLite real", () => {
  beforeEach(async () => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    jest.clearAllMocks();
    mockSendBatchEvents.mockRejectedValue(apiError(400, "validation"));
  });

  it("A_primer_400_segundo_401", async () => {
    await seedEvents(["E1", "E2"]);
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "E1") throw apiError(400, "validation");
      if (payload.eventId === "E2") throw apiError(401, "auth_required");
    });

    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_required");
    expect((await getQueueEventByEventId("E1"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("E2"))?.status).toBe("pending");
    expect((await getQueueEventByEventId("E1"))?.status).not.toBe("pending");
  });

  it("B_primer_422_segundo_500", async () => {
    await seedEvents(["E1", "E2"]);
    mockSendBatchEvents.mockRejectedValue(apiError(422, "validation"));
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "E1") throw apiError(422, "validation");
      if (payload.eventId === "E2") throw apiError(500, "transient");
    });

    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("deferred");
    expect((await getQueueEventByEventId("E1"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("E2"))?.status).toBe("retry");
    expect((await getQueueEventByEventId("E1"))?.status).not.toBe("retry");
  });

  it("C_exito_400_401", async () => {
    await seedEvents(["E1", "E2", "E3"]);
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "E1") return;
      if (payload.eventId === "E2") throw apiError(400, "validation");
      if (payload.eventId === "E3") throw apiError(401, "auth_required");
    });

    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_required");
    expect((await getQueueEventByEventId("E1"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("E2"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("E3"))?.status).toBe("pending");
  });

  it("D_exito_422_500", async () => {
    await seedEvents(["E1", "E2", "E3"]);
    mockSendSingleEvent.mockImplementation(async (payload: { eventId: string }) => {
      if (payload.eventId === "E1") return;
      if (payload.eventId === "E2") throw apiError(422, "validation");
      if (payload.eventId === "E3") throw apiError(500, "transient");
    });

    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("deferred");
    expect((await getQueueEventByEventId("E1"))?.status).toBe("synced");
    expect((await getQueueEventByEventId("E2"))?.status).toBe("permanent_failure");
    expect((await getQueueEventByEventId("E3"))?.status).toBe("retry");
  });
});
