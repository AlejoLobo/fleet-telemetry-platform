jest.mock("expo-secure-store", () => {
  const store = new Map<string, string>();
  return {
    getItemAsync: jest.fn(async (key: string) => store.get(key) ?? null),
    setItemAsync: jest.fn(async (key: string, value: string) => {
      store.set(key, value);
    }),
    deleteItemAsync: jest.fn(async (key: string) => {
      store.delete(key);
    }),
    __store: store,
  };
});

jest.mock("expo-crypto", () => ({
  randomUUID: jest.fn(() => "11111111-1111-1111-1111-111111111111"),
}));

import * as SecureStore from "expo-secure-store";
import {
  loadDriverProfile,
  resetDriverProfileForTests,
  saveDriverProfile,
} from "@/services/driver-profile";

describe("driver-profile", () => {
  beforeEach(async () => {
    (SecureStore as unknown as { __store: Map<string, string> }).__store.clear();
    await resetDriverProfileForTests();
    jest.clearAllMocks();
  });

  it("crea un deviceId estable y lo reutiliza", async () => {
    const first = await loadDriverProfile();
    const second = await loadDriverProfile();
    expect(first.deviceId).toBe("11111111-1111-1111-1111-111111111111");
    expect(second.deviceId).toBe(first.deviceId);
  });

  it("persiste nombre de vehículo y conductor", async () => {
    const saved = await saveDriverProfile({
      vehicleName: "Camión norte",
      driverName: "Ana",
    });
    expect(saved.vehicleName).toBe("Camión norte");
    expect(saved.driverName).toBe("Ana");

    const loaded = await loadDriverProfile();
    expect(loaded.vehicleName).toBe("Camión norte");
    expect(loaded.driverName).toBe("Ana");
    expect(loaded.deviceId).toBe(saved.deviceId);
  });
});
