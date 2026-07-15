const mockStore = new Map<string, string>();
let mockGenerateCounter = 0;

jest.mock("expo-secure-store", () => ({
  getItemAsync: jest.fn(async (key: string) => mockStore.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string) => {
    mockStore.set(key, value);
  }),
  deleteItemAsync: jest.fn(async (key: string) => {
    mockStore.delete(key);
  }),
}));

jest.mock("expo-crypto", () => ({
  randomUUID: jest.fn(() => {
    mockGenerateCounter += 1;
    return `aaaaaaaa-bbbb-4ccc-8ddd-${String(mockGenerateCounter).padStart(12, "0")}`;
  }),
}));

import * as SecureStore from "expo-secure-store";
import * as Crypto from "expo-crypto";
import {
  generateDeviceId,
  isValidDeviceId,
  loadOrCreateDeviceId,
  resetDeviceIdForTests,
} from "@/services/device-id-store";

describe("device-id-store", () => {
  beforeEach(async () => {
    mockStore.clear();
    mockGenerateCounter = 0;
    jest.clearAllMocks();
    (SecureStore.getItemAsync as jest.Mock).mockImplementation(
      async (key: string) => mockStore.get(key) ?? null,
    );
    (SecureStore.setItemAsync as jest.Mock).mockImplementation(
      async (key: string, value: string) => {
        mockStore.set(key, value);
      },
    );
    (SecureStore.deleteItemAsync as jest.Mock).mockImplementation(
      async (key: string) => {
        mockStore.delete(key);
      },
    );
    await resetDeviceIdForTests();
    mockStore.clear();
  });

  it("genera UUID cuando no existe", async () => {
    const id = await loadOrCreateDeviceId();
    expect(isValidDeviceId(id)).toBe(true);
    expect(SecureStore.setItemAsync).toHaveBeenCalled();
  });

  it("devuelve el mismo ID en lecturas posteriores", async () => {
    const first = await loadOrCreateDeviceId();
    const second = await loadOrCreateDeviceId();
    expect(second).toBe(first);
    expect(Crypto.randomUUID).toHaveBeenCalledTimes(1);
  });

  it("dos llamadas concurrentes generan un solo UUID", async () => {
    const [a, b] = await Promise.all([loadOrCreateDeviceId(), loadOrCreateDeviceId()]);
    expect(a).toBe(b);
    expect(isValidDeviceId(a)).toBe(true);
    expect(Crypto.randomUUID).toHaveBeenCalledTimes(1);
  });

  it("lectura y escritura fallan pero llamadas posteriores devuelven el mismo UUID", async () => {
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValue(new Error("read fail"));
    (SecureStore.setItemAsync as jest.Mock).mockRejectedValue(new Error("write fail"));

    const first = await loadOrCreateDeviceId();
    const second = await loadOrCreateDeviceId();
    const third = await loadOrCreateDeviceId();

    expect(first).toBe(second);
    expect(second).toBe(third);
    expect(isValidDeviceId(first)).toBe(true);
    expect(Crypto.randomUUID).toHaveBeenCalledTimes(1);
  });

  it("un valor inválido almacenado se reemplaza", async () => {
    mockStore.set("fleet.device.id", "VH-001");
    const id = await loadOrCreateDeviceId();
    expect(id).not.toBe("VH-001");
    expect(isValidDeviceId(id)).toBe(true);
    expect(Crypto.randomUUID).toHaveBeenCalled();
  });

  it("un UUID válido se conserva", async () => {
    const persisted = "11111111-1111-4111-8111-111111111111";
    mockStore.set("fleet.device.id", persisted);
    const id = await loadOrCreateDeviceId();
    expect(id).toBe(persisted);
    expect(Crypto.randomUUID).not.toHaveBeenCalled();
  });

  it("no acepta nombres antiguos como VH-001", () => {
    expect(isValidDeviceId("VH-001")).toBe(false);
    expect(isValidDeviceId("Camión Pereira")).toBe(false);
    expect(isValidDeviceId("")).toBe(false);
  });

  it("generateDeviceId produce UUID válido", async () => {
    const id = await generateDeviceId();
    expect(isValidDeviceId(id)).toBe(true);
  });

  it("reset limpia SecureStore, caché y promesa", async () => {
    const first = await loadOrCreateDeviceId();
    expect(first).toBeTruthy();
    await resetDeviceIdForTests();
    expect(mockStore.has("fleet.device.id")).toBe(false);

    mockGenerateCounter = 10;
    const second = await loadOrCreateDeviceId();
    expect(second).not.toBe(first);
    expect(isValidDeviceId(second)).toBe(true);
  });
});
