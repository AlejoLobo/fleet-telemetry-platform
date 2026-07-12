import { getAuthRuntimeSnapshot, setAuthRuntimeSnapshot, resetAuthRuntimeForTests } from "@/services/auth-runtime";
import { sendBatchEvents, sendSingleEvent, TelemetryApiError } from "@/services/telemetry-api";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

describe("telemetry-api auth", () => {
  beforeEach(() => {
    mockFetch.mockReset();
    resetAuthRuntimeForTests();
  });

  it("Auth_habilitada_con_token_envia_Bearer_en_batch", async () => {
    setAuthRuntimeSnapshot({ enabled: true, token: "secret-token", tokenExpired: false });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendBatchEvents([
      {
        eventId: "11111111-1111-1111-1111-111111111111",
        vehicleId: "VH-001",
        driverId: "DRV-001",
        timestamp: "2026-07-12T08:00:00Z",
        latitude: 1,
        longitude: 2,
        speedKmh: 3,
        fuelLevelPercent: 4,
        batteryPercent: 5,
      },
    ]);
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer secret-token");
  });

  it("Auth_habilitada_con_token_envia_Bearer_en_single", async () => {
    setAuthRuntimeSnapshot({ enabled: true, token: "secret-token", tokenExpired: false });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      vehicleId: "VH-001",
      driverId: "DRV-001",
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: 4,
      batteryPercent: 5,
    });
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer secret-token");
  });

  it("Auth_deshabilitada_envia_telemetria_sin_Authorization", async () => {
    setAuthRuntimeSnapshot({ enabled: false, token: null, tokenExpired: false });
    mockFetch.mockResolvedValueOnce({ ok: true, text: async () => "" });
    await sendSingleEvent({
      eventId: "11111111-1111-1111-1111-111111111111",
      vehicleId: "VH-001",
      driverId: "DRV-001",
      timestamp: "2026-07-12T08:00:00Z",
      latitude: 1,
      longitude: 2,
      speedKmh: 3,
      fuelLevelPercent: 4,
      batteryPercent: 5,
    });
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers.Authorization).toBeUndefined();
  });

  it("Auth_habilitada_sin_token_no_ejecuta_fetch", async () => {
    setAuthRuntimeSnapshot({ enabled: true, token: null, tokenExpired: false });
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

  it("El_token_no_aparece_en_mensajes_de_error", async () => {
    setAuthRuntimeSnapshot({ enabled: true, token: "secret-token", tokenExpired: false });
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
