import { getAuthRuntimeSnapshot, resetAuthRuntimeForTests, setAuthRuntimeSnapshot } from "@/services/auth-runtime";
import { sendBatchEvents, sendSingleEvent, TelemetryApiError } from "@/services/telemetry-api";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

describe("telemetry-api auth", () => {
  beforeEach(() => {
    mockFetch.mockReset();
    resetAuthRuntimeForTests();
  });

  it("Auth_habilitada_con_token_envia_Bearer_en_batch", async () => {
    setAuthRuntimeSnapshot({
      mode: "enabled",
      token: "secret-token",
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
      tokenExpired: false,
    });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendBatchEvents([
      {
        eventId: "11111111-1111-1111-1111-111111111111",
        deviceId: "11111111-1111-1111-1111-111111111111",
        driverId: "DRV-001",
        timestamp: "2026-07-12T08:00:00Z",
        latitude: 1,
        longitude: 2,
        speedKmh: 3,
        fuelLevelPercent: 4,
        batteryPercent: 5,
      },
    ], "aaaaaaaa-bbbb-4ccc-8ddd-000000000002");
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer secret-token");
  });

  it("Auth_habilitada_con_token_envia_Bearer_en_single", async () => {
    setAuthRuntimeSnapshot({
      mode: "enabled",
      token: "secret-token",
      expiresAtIso: new Date(Date.now() + 60_000).toISOString(),
      tokenExpired: false,
    });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: "11111111-1111-1111-1111-111111111111",
      driverId: "DRV-001",
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: 4,
      batteryPercent: 5,
    }, "aaaaaaaa-bbbb-4ccc-8ddd-000000000002");
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer secret-token");
  });

  it("Auth_deshabilitada_envia_telemetria_sin_Authorization", async () => {
    setAuthRuntimeSnapshot({ mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: "11111111-1111-1111-1111-111111111111",
      driverId: "DRV-001",
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: 4,
      batteryPercent: 5,
    }, "aaaaaaaa-bbbb-4ccc-8ddd-000000000002");
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBeUndefined();
  });

  it("Auth_habilitada_sin_token_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({ mode: "enabled", token: null, expiresAtIso: null, tokenExpired: false });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: "11111111-1111-1111-1111-111111111111",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, "aaaaaaaa-bbbb-4ccc-8ddd-000000000002")).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("Auth_mode_unknown_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({ mode: "unknown", token: null, expiresAtIso: null, tokenExpired: false });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: "11111111-1111-1111-1111-111111111111",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, "aaaaaaaa-bbbb-4ccc-8ddd-000000000002")).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("Token_expirado_antes_de_peticion_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({
      mode: "enabled",
      token: "secret-token",
      expiresAtIso: new Date(Date.now() - 60_000).toISOString(),
      tokenExpired: true,
    });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: "11111111-1111-1111-1111-111111111111",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, "aaaaaaaa-bbbb-4ccc-8ddd-000000000002")).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("El_token_no_aparece_en_mensajes_de_error", async () => {
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
        deviceId: "11111111-1111-1111-1111-111111111111",
        driverId: null,
        timestamp: "2026-07-12T08:00:00Z",
        latitude: 1,
        longitude: 2,
        speedKmh: 3,
        fuelLevelPercent: null,
        batteryPercent: null,
      }, "aaaaaaaa-bbbb-4ccc-8ddd-000000000002");
    } catch (error) {
      expect((error as TelemetryApiError).sanitizedMessage).not.toContain("secret-token");
    }
  });

  it("X-Device-Id coincide con el DeviceId del payload", async () => {
    setAuthRuntimeSnapshot({ mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    const deviceId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    await sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId,
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, deviceId);
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string) as { deviceId: string };
    expect(headers["X-Device-Id"]).toBe(deviceId);
    expect(body.deviceId).toBe(deviceId);
  });

  it("batch utiliza el mismo ID estable", async () => {
    setAuthRuntimeSnapshot({ mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendBatchEvents([{
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: "11111111-1111-1111-1111-111111111111",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }], "phys-device-stable-01");
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers["X-Device-Id"]).toBe("phys-device-stable-01");
  });

  it("no envía encabezado vacío", async () => {
    setAuthRuntimeSnapshot({ mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false });
    await expect(sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      deviceId: "11111111-1111-1111-1111-111111111111",
      driverId: null,
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: null,
      batteryPercent: null,
    }, "   ")).rejects.toBeInstanceOf(TelemetryApiError);
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
