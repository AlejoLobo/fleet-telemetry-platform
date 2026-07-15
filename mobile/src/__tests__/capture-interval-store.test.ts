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

import * as SecureStore from "expo-secure-store";
import {
  loadCaptureIntervalSeconds,
  resetCaptureIntervalForTests,
  saveCaptureIntervalSeconds,
} from "@/services/capture-interval-store";

describe("capture-interval-store", () => {
  beforeEach(async () => {
    await resetCaptureIntervalForTests();
    jest.clearAllMocks();
  });

  it("persiste y recupera el intervalo", async () => {
    await saveCaptureIntervalSeconds(10);
    expect(await loadCaptureIntervalSeconds()).toBe(10);
    expect(SecureStore.setItemAsync).toHaveBeenCalledWith(
      "fleet.profile.captureIntervalSeconds",
      "10",
    );
  });

  it("valor inválido en store usa 5", async () => {
    await SecureStore.setItemAsync("fleet.profile.captureIntervalSeconds", "9");
    expect(await loadCaptureIntervalSeconds()).toBe(5);
  });
});
