import {
  clearDeviceProfileForTests,
  loadCachedVehicleType,
  loadLocalDeviceProfile,
  markDeviceRegistered,
  saveCachedVehicleType,
} from "@/services/device-profile-store";
import { normalizeVehicleType, vehicleTypeLabel } from "@/types/vehicle";

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

describe("vehicle type catalog and profile store", () => {
  beforeEach(async () => {
    mockStore.clear();
    await clearDeviceProfileForTests();
  });

  it("perfil antiguo sin tipo → car", async () => {
    expect(await loadCachedVehicleType()).toBe("car");
    const profile = await loadLocalDeviceProfile();
    expect(profile.vehicleType).toBe("car");
  });

  it("seleccionar Motocicleta → motorcycle y persiste", async () => {
    expect(vehicleTypeLabel("motorcycle")).toBe("Motocicleta");
    await saveCachedVehicleType("motorcycle");
    expect(await loadCachedVehicleType()).toBe("motorcycle");
  });

  it("markDeviceRegistered guarda nombre y tipo", async () => {
    await markDeviceRegistered("ffffffff-ffff-4fff-8fff-ffffffffffff", "VH-009", "bus");
    const profile = await loadLocalDeviceProfile();
    expect(profile.vehicleName).toBe("VH-009");
    expect(profile.vehicleType).toBe("bus");
  });

  it("normalizeVehicleType rechaza inválidos con default car", () => {
    expect(normalizeVehicleType("MOTORCYCLE")).toBe("motorcycle");
    expect(normalizeVehicleType("boat")).toBe("car");
    expect(normalizeVehicleType(undefined)).toBe("car");
  });
});
