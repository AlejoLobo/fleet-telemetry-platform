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
  DeviceIdentityStorageError,
  generateDeviceId,
  isValidDeviceId,
  loadOrCreateDeviceId,
  resetDeviceIdForTests,
  setCachedDeviceIdForTests,
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

  it("UUID válido almacenado se reutiliza", async () => {
    const persisted = "11111111-1111-4111-8111-111111111111";
    mockStore.set("fleet.device.id", persisted);
    const id = await loadOrCreateDeviceId();
    expect(id).toBe(persisted);
    expect(Crypto.randomUUID).not.toHaveBeenCalled();
  });

  it("SecureStore null genera y persiste UUID nuevo", async () => {
    const id = await loadOrCreateDeviceId();
    expect(isValidDeviceId(id)).toBe(true);
    expect(SecureStore.setItemAsync).toHaveBeenCalledWith("fleet.device.id", id);
  });

  it("dos llamadas concurrentes generan un solo UUID", async () => {
    const [a, b] = await Promise.all([loadOrCreateDeviceId(), loadOrCreateDeviceId()]);
    expect(a).toBe(b);
    expect(Crypto.randomUUID).toHaveBeenCalledTimes(1);
  });

  it("fallo de lectura sin caché produce error y no genera UUID", async () => {
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValue(new Error("read fail"));
    await expect(loadOrCreateDeviceId()).rejects.toBeInstanceOf(DeviceIdentityStorageError);
    expect(Crypto.randomUUID).not.toHaveBeenCalled();
  });

  it("fallo de lectura con caché reutiliza la caché", async () => {
    const cached = "22222222-2222-4222-8222-222222222222";
    setCachedDeviceIdForTests(cached);
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValue(new Error("read fail"));
    const id = await loadOrCreateDeviceId();
    expect(id).toBe(cached);
    expect(Crypto.randomUUID).not.toHaveBeenCalled();
  });

  it("fallo de escritura produce error y no confirma identidad en caché", async () => {
    (SecureStore.setItemAsync as jest.Mock).mockRejectedValue(new Error("write fail"));
    await expect(loadOrCreateDeviceId()).rejects.toMatchObject({
      name: "DeviceIdentityStorageError",
      operation: "write",
    });
    // Reintento sin éxito de escritura: sigue fallando (sin identidad confirmada).
    await expect(loadOrCreateDeviceId()).rejects.toBeInstanceOf(DeviceIdentityStorageError);
    expect(mockStore.size).toBe(0);
  });

  it("fallo de lectura sin caché bloquea registro remoto (no genera UUID usable)", async () => {
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValue(new Error("read fail"));
    let caught: unknown;
    try {
      await loadOrCreateDeviceId();
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(DeviceIdentityStorageError);
    expect(Crypto.randomUUID).not.toHaveBeenCalled();
    // Sin UUID no hay ensureDeviceRegistered / syncPendingQueue con identidad válida.
  });

  it("un fallo no deja la promesa bloqueada permanentemente", async () => {
    (SecureStore.getItemAsync as jest.Mock)
      .mockRejectedValueOnce(new Error("transient"))
      .mockImplementation(async (key: string) => mockStore.get(key) ?? null);

    await expect(loadOrCreateDeviceId()).rejects.toBeInstanceOf(DeviceIdentityStorageError);
    const recovered = await loadOrCreateDeviceId();
    expect(isValidDeviceId(recovered)).toBe(true);
  });

  it("valor inválido VH-001 se reemplaza tras lectura OK", async () => {
    mockStore.set("fleet.device.id", "VH-001");
    const id = await loadOrCreateDeviceId();
    expect(id).not.toBe("VH-001");
    expect(isValidDeviceId(id)).toBe(true);
  });

  it("rechaza UUID nil y nombres no UUID", () => {
    expect(isValidDeviceId("00000000-0000-0000-0000-000000000000")).toBe(false);
    expect(isValidDeviceId("VH-001")).toBe(false);
    expect(isValidDeviceId("Camión")).toBe(false);
    expect(isValidDeviceId("not-a-uuid")).toBe(false);
  });

  it("generateDeviceId produce UUID válido", async () => {
    expect(isValidDeviceId(await generateDeviceId())).toBe(true);
  });

  it("reset limpia SecureStore y caché", async () => {
    const first = await loadOrCreateDeviceId();
    await resetDeviceIdForTests();
    expect(mockStore.has("fleet.device.id")).toBe(false);
    mockGenerateCounter = 10;
    const second = await loadOrCreateDeviceId();
    expect(second).not.toBe(first);
  });
});
