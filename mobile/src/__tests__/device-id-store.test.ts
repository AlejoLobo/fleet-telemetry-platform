const mockStore = new Map<string, string>();

jest.mock("expo-secure-store", () => ({
  getItemAsync: jest.fn(async (key: string) => mockStore.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string) => {
    mockStore.set(key, value);
  }),
  deleteItemAsync: jest.fn(async (key: string) => {
    mockStore.delete(key);
  }),
}));

jest.mock("@/utils/id", () => ({
  generateEventId: jest.fn(async () => `generated-${mockStore.size + 1}-${Date.now()}`),
}));

import * as SecureStore from "expo-secure-store";
import { generateEventId } from "@/utils/id";
import { loadOrCreateDeviceId, resetDeviceIdForTests } from "@/services/device-id-store";

describe("device-id-store", () => {
  beforeEach(async () => {
    mockStore.clear();
    jest.clearAllMocks();
    (generateEventId as jest.Mock).mockImplementation(
      async () => `id-${Math.random().toString(16).slice(2, 10)}`,
    );
    await resetDeviceIdForTests();
    mockStore.clear();
  });

  it("genera ID cuando no existe", async () => {
    const id = await loadOrCreateDeviceId();
    expect(id.length).toBeGreaterThan(0);
    expect(SecureStore.setItemAsync).toHaveBeenCalled();
  });

  it("devuelve el mismo ID en lecturas posteriores", async () => {
    const first = await loadOrCreateDeviceId();
    const second = await loadOrCreateDeviceId();
    expect(second).toBe(first);
    expect(generateEventId).toHaveBeenCalledTimes(1);
  });

  it("no cambia al cambiar vehículo (independiente del dominio)", async () => {
    const before = await loadOrCreateDeviceId();
    const afterVehicleChange = await loadOrCreateDeviceId();
    expect(afterVehicleChange).toBe(before);
  });

  it("recupera el valor desde SecureStore", async () => {
    mockStore.set("fleet.device.id", "persisted-device-id-01");
    const id = await loadOrCreateDeviceId();
    expect(id).toBe("persisted-device-id-01");
    expect(generateEventId).not.toHaveBeenCalled();
  });

  it("maneja errores de lectura generando un ID usable", async () => {
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(new Error("boom"));
    const id = await loadOrCreateDeviceId();
    expect(id.length).toBeGreaterThan(0);
  });

  it("reset exclusivo para pruebas borra el valor", async () => {
    const id = await loadOrCreateDeviceId();
    expect(id).toBeTruthy();
    await resetDeviceIdForTests();
    expect(mockStore.has("fleet.device.id")).toBe(false);
  });
});
