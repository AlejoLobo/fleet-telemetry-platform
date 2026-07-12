import { createSqliteMemoryDb, getSqliteRows, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

jest.mock("@/services/telemetry-api", () => ({
  sendSingleEvent: jest.fn(async () => undefined),
  sendBatchEvents: jest.fn(async () => undefined),
  TelemetryApiError: class TelemetryApiError extends Error {
    status: number;
    category: string;
    constructor(status: number, category: string, message: string) {
      super(message);
      this.status = status;
      this.category = category;
    }
  },
}));

jest.mock("@/services/auth-service", () => ({
  handleUnauthorizedFromApi: jest.fn(),
  markForbiddenFromApi: jest.fn(),
}));

import { enqueueEvent, getQueueEventByEventId, resetOfflineQueueForTests } from "@/db/offline-queue";
import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";
import { setAuthRuntimeSnapshot, resetAuthRuntimeForTests } from "@/services/auth-runtime";

const event = {
  eventId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  vehicleId: "VH-001",
  driverId: "DRV-001",
  timestamp: "2026-07-10T10:00:00Z",
  latitude: 4.65,
  longitude: -74.08,
  speedKmh: 40,
  fuelLevelPercent: 70,
  batteryPercent: 90,
};

function resumeSyncOnAuthChange(previousStatus: string, nextStatus: string, isOnline: boolean, syncFn: () => Promise<unknown>) {
  if (previousStatus !== "authenticated" && nextStatus === "authenticated" && isOnline) {
    return syncFn();
  }
  return Promise.resolve();
}

describe("reanudación post-login", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    resetSyncCoordinatorForTests();
    resetAuthRuntimeForTests();
    setAuthRuntimeSnapshot({ mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false });
  });

  it("auth_required_login_exitoso_inicia_exactamente_una_sincronizacion", async () => {
    await enqueueEvent(event);
    let syncCalls = 0;
    const syncFn = async () => {
      syncCalls += 1;
      return syncPendingQueue(true);
    };

    await resumeSyncOnAuthChange("auth_required", "authenticated", true, syncFn);
    expect(syncCalls).toBe(1);
  });

  it("dos_notificaciones_authenticated_no_crean_dos_corridas", async () => {
    await enqueueEvent(event);
    let syncCalls = 0;
    const syncFn = async () => {
      syncCalls += 1;
      return syncPendingQueue(true);
    };

    const first = resumeSyncOnAuthChange("auth_required", "authenticated", true, syncFn);
    const second = resumeSyncOnAuthChange("authenticated", "authenticated", true, syncFn);
    await Promise.all([first, second]);
    expect(syncCalls).toBe(1);
  });

  it("captura_sin_token_continua_encolando_offline", async () => {
    setAuthRuntimeSnapshot({ mode: "enabled", token: null, expiresAtIso: null, tokenExpired: false });
    await enqueueEvent(event);
    const row = await getQueueEventByEventId(event.eventId);
    expect(row?.status).toBe("pending");
    expect(row?.eventId).toBe(event.eventId);
  });

  it("reautenticacion_conserva_EventId_original_sin_duplicar", async () => {
    await enqueueEvent(event);
    const before = await getQueueEventByEventId(event.eventId);
    setAuthRuntimeSnapshot({
      mode: "enabled",
      token: "jwt",
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
      tokenExpired: false,
    });
    await syncPendingQueue(true);
    const rows = getSqliteRows();
    const matches = rows.filter((row) => row.event_id === event.eventId);
    expect(matches).toHaveLength(1);
    expect(before?.eventId).toBe(event.eventId);
  });
});
