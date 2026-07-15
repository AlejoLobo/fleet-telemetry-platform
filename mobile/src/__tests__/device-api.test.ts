import { resetAuthRuntimeForTests, setAuthRuntimeSnapshot } from "@/services/auth-runtime";
import { registerDevice, renameDevice } from "@/services/device-api";
import { TelemetryApiError } from "@/services/telemetry-api";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

const DEVICE_ID = "dddddddd-dddd-dddd-dddd-dddddddddddd";

describe("device-api", () => {
  beforeEach(() => {
    mockFetch.mockReset();
    resetAuthRuntimeForTests();
    setAuthRuntimeSnapshot({ mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false });
  });

  it("registerDevice llama POST /api/devices/register con DeviceId", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({ deviceId: DEVICE_ID, vehicleName: "VH-001" }),
    });

    const profile = await registerDevice(DEVICE_ID);

    expect(profile).toEqual({ deviceId: DEVICE_ID, vehicleName: "VH-001" });
    expect(mockFetch.mock.calls[0][0]).toContain("/api/devices/register");
    expect(mockFetch.mock.calls[0][1]?.method).toBe("POST");
    const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string) as { deviceId: string };
    expect(body.deviceId).toBe(DEVICE_ID);
    const headers = mockFetch.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers["X-Device-Id"]).toBe(DEVICE_ID);
  });

  it("renameDevice llama PATCH name sin cambiar DeviceId", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({ deviceId: DEVICE_ID, vehicleName: "Camión Norte" }),
    });

    const profile = await renameDevice(DEVICE_ID, "Camión Norte");

    expect(profile.deviceId).toBe(DEVICE_ID);
    expect(profile.vehicleName).toBe("Camión Norte");
    expect(mockFetch.mock.calls[0][0]).toContain(`/api/devices/${DEVICE_ID}/name`);
    expect(mockFetch.mock.calls[0][1]?.method).toBe("PATCH");
  });

  it("no genera VH-### en el cliente", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: async () => JSON.stringify({ deviceId: DEVICE_ID, vehicleName: "VH-042" }),
    });
    await registerDevice(DEVICE_ID);
    const body = JSON.parse(mockFetch.mock.calls[0][1]?.body as string) as Record<string, unknown>;
    expect(body).not.toHaveProperty("vehicleName");
    expect(Object.keys(body)).toEqual(["deviceId"]);
  });

  it("propaga conflict 409 al renombrar", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 409,
      headers: { get: () => null },
      text: async () => "name taken",
    });
    await expect(renameDevice(DEVICE_ID, "Duplicado")).rejects.toMatchObject({
      status: 409,
      category: "protocol",
    } satisfies Partial<TelemetryApiError>);
  });
});
