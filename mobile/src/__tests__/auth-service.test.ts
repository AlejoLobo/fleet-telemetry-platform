import { createSqliteMemoryDb, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

import {
  configureAuthTokenStore,
  initializeAuthSession,
  login,
  logout,
  markForbiddenFromApi,
  resetAuthServiceForTests,
} from "@/services/auth-service";
import { InMemoryAuthTokenStore } from "@/services/auth-token-store";
import { getAuthRuntimeSnapshot } from "@/services/auth-runtime";
import { enqueueEvent, getQueueEventByEventId, resetOfflineQueueForTests } from "@/db/offline-queue";
import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

const queueEvent = {
  eventId: "99999999-9999-9999-9999-999999999999",
  vehicleId: "VH-001",
  driverId: "DRV-001",
  timestamp: "2026-07-10T10:00:00Z",
  latitude: 4.65,
  longitude: -74.08,
  speedKmh: 40,
  fuelLevelPercent: 70,
  batteryPercent: 90,
};

describe("auth-service con cola SQLite real", () => {
  beforeEach(() => {
    resetAuthServiceForTests();
    resetSqliteMemory();
    resetOfflineQueueForTests();
    resetSyncCoordinatorForTests();
    configureAuthTokenStore(new InMemoryAuthTokenStore());
    mockFetch.mockReset();
  });

  it("Logout_elimina_token_pero_no_modifica_cola", async () => {
    await enqueueEvent(queueEvent);
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({ token: "jwt", expiresAtIso: new Date(Date.now() + 60_000).toISOString() });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    await logout();

    expect(await store.load()).toBeNull();
    const row = await getQueueEventByEventId(queueEvent.eventId);
    expect(row?.eventId).toBe(queueEvent.eventId);
    expect(row?.status).toBe("pending");
    expect(row?.latitude).toBe(queueEvent.latitude);
  });

  it("Respuesta_403_conserva_token_y_cola", async () => {
    await enqueueEvent(queueEvent);
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({ token: "jwt", expiresAtIso: new Date(Date.now() + 60_000).toISOString() });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    markForbiddenFromApi("forbidden");

    expect(await store.load()).not.toBeNull();
    const row = await getQueueEventByEventId(queueEvent.eventId);
    expect(row?.eventId).toBe(queueEvent.eventId);
    expect(row?.status).toBe("pending");
    expect(row?.status).not.toBe("permanent_failure");
  });
});

describe("auth fail-closed y expiración", () => {
  beforeEach(() => {
    resetAuthServiceForTests();
    configureAuthTokenStore(new InMemoryAuthTokenStore());
    mockFetch.mockReset();
  });

  it("auth_status_timeout_bloquea_telemetria", async () => {
    mockFetch.mockRejectedValueOnce(Object.assign(new Error("Timeout"), { name: "AbortError" }));
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("unknown");
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_status_error");
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it("auth_status_500_bloquea_telemetria", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500, json: async () => ({}) });
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("unknown");
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_status_error");
  });

  it("auth_status_objeto_vacio_bloquea_telemetria", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({}) });
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("unknown");
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_status_error");
  });

  it("auth_status_enabled_string_bloquea_telemetria", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: "false" }) });
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("unknown");
    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_status_error");
  });

  it("solo_enabled_false_permite_envio_anonimo", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: false }) });
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("disabled");
  });

  it("expiracion_local_bloquea_sync_sin_fetch", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    const expiredIso = new Date(Date.now() - 60_000).toISOString();
    await store.save({ token: "jwt", expiresAtIso: expiredIso });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();

    const result = await syncPendingQueue(true, "test-device-id-001");
    expect(result.status).toBe("auth_required");
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });
});
