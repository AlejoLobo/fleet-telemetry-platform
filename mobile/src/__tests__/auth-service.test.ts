import { createSqliteMemoryDb, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

import {
  canSyncTelemetryForDevice,
  configureAuthTokenStore,
  enrollDevice,
  getAuthSessionSnapshot,
  initializeAuthSession,
  login,
  logout,
  markForbiddenFromApi,
  resetAuthServiceForTests,
  validateSessionForLocalDevice,
} from "@/services/auth-service";
import { InMemoryAuthTokenStore } from "@/services/auth-token-store";
import { getAuthRuntimeSnapshot } from "@/services/auth-runtime";
import { enqueueEvent, getQueueEventByEventId, resetOfflineQueueForTests } from "@/db/offline-queue";

jest.mock("@/services/device-registry", () => ({
  ensureDeviceRegistered: jest.fn(async (deviceId: string) => ({
    deviceId,
    vehicleName: "VH-001", vehicleType: "car",
  })),
  updateVehicleDisplayName: jest.fn(),
}));

import { resetSyncCoordinatorForTests, syncPendingQueue } from "@/services/offline-sync-coordinator";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

const DEVICE_ID = "aaaaaaaa-bbbb-4ccc-8ddd-000000000001";

function makeJwt(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.sig`;
}

function makeDeviceJwt(deviceId = DEVICE_ID): string {
  return makeJwt({
    device_id: deviceId,
    role: "device",
    permission: "telemetry:write",
  });
}

function makeOperatorJwt(withManage = false): string {
  return makeJwt({
    role: "operator",
    permission: withManage
      ? ["fleet:read", "alert:acknowledge", "ai:query", "operations:read", "device:manage"]
      : ["fleet:read", "alert:acknowledge", "ai:query", "operations:read"],
    unique_name: "admin",
  });
}

const queueEvent = {
  eventId: "99999999-9999-9999-9999-999999999999",
  deviceId: DEVICE_ID,
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
    await store.save({
      token: makeDeviceJwt(),
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
    });
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
    await store.save({
      token: makeDeviceJwt(),
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
    });
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
    const result = await syncPendingQueue(true, DEVICE_ID);
    expect(result.status).toBe("auth_status_error");
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it("auth_status_500_bloquea_telemetria", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500, json: async () => ({}) });
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("unknown");
    const result = await syncPendingQueue(true, DEVICE_ID);
    expect(result.status).toBe("auth_status_error");
  });

  it("auth_status_objeto_vacio_bloquea_telemetria", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({}) });
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("unknown");
    const result = await syncPendingQueue(true, DEVICE_ID);
    expect(result.status).toBe("auth_status_error");
  });

  it("auth_status_enabled_string_bloquea_telemetria", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: "false" }) });
    await initializeAuthSession();
    expect(getAuthRuntimeSnapshot().mode).toBe("unknown");
    const result = await syncPendingQueue(true, DEVICE_ID);
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
    await store.save({ token: makeDeviceJwt(), expiresAtIso: expiredIso });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();

    const result = await syncPendingQueue(true, DEVICE_ID);
    expect(result.status).toBe("auth_required");
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });
});

describe("enrolamiento de dispositivo", () => {
  beforeEach(() => {
    resetAuthServiceForTests();
    configureAuthTokenStore(new InMemoryAuthTokenStore());
    mockFetch.mockReset();
  });

  it("enrollDevice almacena token de dispositivo y habilita canSync", async () => {
    const token = makeDeviceJwt(DEVICE_ID);
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ token, expiresInMinutes: 60, deviceId: DEVICE_ID }),
    });

    const snapshot = await enrollDevice(DEVICE_ID, "admin", "secret");
    expect(snapshot.status).toBe("authenticated");
    expect(snapshot.sessionKind).toBe("device");
    expect(snapshot.deviceId).toBe(DEVICE_ID);
    expect(snapshot.permissions).toContain("telemetry:write");
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(true);
    expect(canSyncTelemetryForDevice("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")).toBe(false);
  });

  it("login de operador no habilita canSync de telemetría", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ token: makeOperatorJwt(), expiresInMinutes: 60 }),
    });
    await login("admin", "secret");
    expect(getAuthSessionSnapshot().sessionKind).toBe("operator");
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(false);
  });

  it("reiniciar app restaura sesión de dispositivo válida", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({
      token: makeDeviceJwt(DEVICE_ID),
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
    });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    expect(getAuthSessionSnapshot().sessionKind).toBe("device");
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(true);
  });

  it("token de operador almacenado no restaura sesión sync-ready", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({
      token: makeOperatorJwt(),
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
    });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    expect(getAuthSessionSnapshot().status).toBe("auth_required");
    expect(await store.load()).toBeNull();
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(false);
  });

  it("token A + DeviceId B invalida sesión y conserva la cola", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    const foreignDeviceId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    await store.save({
      token: makeDeviceJwt(foreignDeviceId),
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
    });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    expect(getAuthSessionSnapshot().status).toBe("authenticated");
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(false);

    await enqueueEvent(queueEvent, "simulated");
    expect(await getQueueEventByEventId(queueEvent.eventId)).not.toBeNull();

    const snapshot = await validateSessionForLocalDevice(DEVICE_ID);
    expect(snapshot.status).toBe("auth_required");
    expect(snapshot.statusMessage).toMatch(/otro dispositivo/i);
    expect(await store.load()).toBeNull();
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(false);
    expect(await getQueueEventByEventId(queueEvent.eventId)).not.toBeNull();
  });

  it("token A + DeviceId A permanece válido", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({
      token: makeDeviceJwt(DEVICE_ID),
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
    });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    const snapshot = await validateSessionForLocalDevice(DEVICE_ID);
    expect(snapshot.status).toBe("authenticated");
    expect(await store.load()).not.toBeNull();
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(true);
  });

  it("auth deshabilitada continúa funcionando sin invalidar", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: false }) });
    await initializeAuthSession();
    const snapshot = await validateSessionForLocalDevice(DEVICE_ID);
    expect(snapshot.status).toBe("auth_disabled");
    expect(canSyncTelemetryForDevice(DEVICE_ID)).toBe(true);
  });
});
