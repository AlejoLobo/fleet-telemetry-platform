import {
  configureAuthTokenStore,
  getAuthSessionSnapshot,
  initializeAuthSession,
  resetAuthServiceForTests,
} from "@/services/auth-service";
import { InMemoryAuthTokenStore } from "@/services/auth-token-store";
import { getAuthRuntimeSnapshot, resetAuthRuntimeForTests } from "@/services/auth-runtime";
import { sendSingleEvent, TelemetryApiError } from "@/services/telemetry-api";
import { setAuthRuntimeSnapshot } from "@/services/auth-runtime";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

describe("expiración centralizada", () => {
  beforeEach(() => {
    resetAuthServiceForTests();
    resetAuthRuntimeForTests();
    configureAuthTokenStore(new InMemoryAuthTokenStore());
    mockFetch.mockReset();
  });

  it("Stored_token_con_expiracion_invalida_se_elimina", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({ token: "jwt", expiresAtIso: "not-a-date" });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    expect(await store.load()).toBeNull();
    expect(getAuthSessionSnapshot().status).toBe("session_expired");
  });

  it("Token_runtime_sin_expiracion_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({ mode: "enabled", token: "secret", expiresAtIso: null, tokenExpired: false });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      vehicleId: "VH-001",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    })).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("Token_runtime_con_expiracion_vacia_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({ mode: "enabled", token: "secret", expiresAtIso: "  ", tokenExpired: false });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      vehicleId: "VH-001",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    })).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("Token_runtime_con_expiracion_invalida_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({ mode: "enabled", token: "secret", expiresAtIso: "invalid", tokenExpired: false });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      vehicleId: "VH-001",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    })).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("Token_runtime_vencido_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({
      mode: "enabled",
      token: "secret",
      expiresAtIso: new Date(Date.now() - 60_000).toISOString(),
      tokenExpired: true,
    });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      vehicleId: "VH-001",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    })).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("Token_runtime_vigente_envia_Bearer", async () => {
    setAuthRuntimeSnapshot({
      mode: "enabled",
      token: "secret-token",
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
      tokenExpired: false,
    });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      vehicleId: "VH-001",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    });
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer secret-token");
  });

  it("Ningun_error_expone_el_token", async () => {
    setAuthRuntimeSnapshot({
      mode: "enabled",
      token: "secret-token",
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
      tokenExpired: false,
    });
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      headers: { get: () => null },
      text: async () => "Bearer secret-token invalid",
    });
    try {
      await sendSingleEvent({
        eventId: "11111111-1111-1111-1111-111111111111",
        vehicleId: "VH-001",
        driverId: null,
        timestamp: "2026-07-12T08:00:00Z",
        latitude: 1,
        longitude: 2,
        speedKmh: 3,
        fuelLevelPercent: null,
        batteryPercent: null,
      });
    } catch (error) {
      expect((error as TelemetryApiError).sanitizedMessage).not.toContain("secret-token");
    }
  });
});
