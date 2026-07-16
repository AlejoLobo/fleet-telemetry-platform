const mockRegisterDevice = jest.fn();
const mockUpdateDeviceProfile = jest.fn();
const mockLoadCachedVehicleName = jest.fn();
const mockLoadCachedVehicleType = jest.fn();
const mockLoadLocalDeviceProfile = jest.fn();
const mockMarkDeviceRegistered = jest.fn();
const mockSaveCachedVehicleName = jest.fn();
const mockSaveCachedVehicleType = jest.fn();

jest.mock("@/services/device-api", () => ({
  registerDevice: (...args: unknown[]) => mockRegisterDevice(...args),
  updateDeviceProfile: (...args: unknown[]) => mockUpdateDeviceProfile(...args),
}));

jest.mock("@/services/device-profile-store", () => ({
  loadCachedVehicleName: () => mockLoadCachedVehicleName(),
  loadCachedVehicleType: () => mockLoadCachedVehicleType(),
  loadLocalDeviceProfile: () => mockLoadLocalDeviceProfile(),
  markDeviceRegistered: (...args: unknown[]) => mockMarkDeviceRegistered(...args),
  saveCachedVehicleName: (...args: unknown[]) => mockSaveCachedVehicleName(...args),
  saveCachedVehicleType: (...args: unknown[]) => mockSaveCachedVehicleType(...args),
}));

import { TelemetryApiError } from "@/services/telemetry-api";
import {
  ensureDeviceRegistered,
  resetDeviceRegistryForTests,
  updateVehicleDisplayName,
  updateVehicleProfile,
} from "@/services/device-registry";

const DEVICE_ID = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee";

describe("device-registry", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    resetDeviceRegistryForTests();
    mockLoadCachedVehicleName.mockResolvedValue(null);
    mockLoadCachedVehicleType.mockResolvedValue("car");
    mockLoadLocalDeviceProfile.mockResolvedValue({ vehicleName: null, vehicleType: "car" });
    mockRegisterDevice.mockResolvedValue({
      deviceId: DEVICE_ID,
      vehicleName: "VH-007",
      vehicleType: "car",
    });
    mockUpdateDeviceProfile.mockResolvedValue({
      deviceId: DEVICE_ID,
      vehicleName: "Unidad Sur",
      vehicleType: "van",
    });
    mockMarkDeviceRegistered.mockResolvedValue(undefined);
    mockSaveCachedVehicleName.mockResolvedValue(undefined);
    mockSaveCachedVehicleType.mockResolvedValue(undefined);
  });

  it("caché existente y servidor disponible: confirma registro", async () => {
    mockLoadCachedVehicleName.mockResolvedValue("VH-OLD");
    const profile = await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledWith(DEVICE_ID, "car");
    expect(profile.vehicleName).toBe("VH-007");
    expect(mockMarkDeviceRegistered).toHaveBeenCalledWith(DEVICE_ID, "VH-007", "car");
    expect(mockSaveCachedVehicleName).not.toHaveBeenCalled();
  });

  it("perfil antiguo sin tipo usa car", async () => {
    mockLoadCachedVehicleType.mockResolvedValue("car");
    await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledWith(DEVICE_ID, "car");
  });

  it("registro con motorcycle envía el tipo", async () => {
    mockRegisterDevice.mockResolvedValue({
      deviceId: DEVICE_ID,
      vehicleName: "VH-008",
      vehicleType: "motorcycle",
    });
    const profile = await ensureDeviceRegistered(DEVICE_ID, "motorcycle");
    expect(mockRegisterDevice).toHaveBeenCalledWith(DEVICE_ID, "motorcycle");
    expect(profile.vehicleType).toBe("motorcycle");
    expect(mockMarkDeviceRegistered).toHaveBeenCalledWith(DEVICE_ID, "VH-008", "motorcycle");
  });

  it("backend reiniciado: vuelve a registrar", async () => {
    await ensureDeviceRegistered(DEVICE_ID);
    await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledTimes(2);
  });

  it("dos llamadas concurrentes producen un solo POST", async () => {
    let resolveRegister!: (value: {
      deviceId: string;
      vehicleName: string;
      vehicleType: string;
    }) => void;
    mockRegisterDevice.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveRegister = resolve;
        }),
    );

    const p1 = ensureDeviceRegistered(DEVICE_ID);
    const p2 = ensureDeviceRegistered(DEVICE_ID);
    await Promise.resolve();
    await Promise.resolve();
    expect(mockRegisterDevice).toHaveBeenCalledTimes(1);
    resolveRegister({ deviceId: DEVICE_ID, vehicleName: "VH-001", vehicleType: "car" });
    const [a, b] = await Promise.all([p1, p2]);
    expect(a.vehicleName).toBe("VH-001");
    expect(b.vehicleName).toBe("VH-001");
    expect(mockRegisterDevice).toHaveBeenCalledTimes(1);
  });

  it("cambiar tipo no cambia DeviceId", async () => {
    mockLoadLocalDeviceProfile.mockResolvedValue({
      vehicleName: "VH-007",
      vehicleType: "car",
    });
    mockUpdateDeviceProfile.mockResolvedValue({
      deviceId: DEVICE_ID,
      vehicleName: "VH-007",
      vehicleType: "pickup",
    });

    const profile = await updateVehicleProfile(DEVICE_ID, {
      vehicleName: "VH-007",
      vehicleType: "pickup",
    });

    expect(profile.deviceId).toBe(DEVICE_ID);
    expect(profile.vehicleType).toBe("pickup");
  });

  it("error HTTP conserva perfil anterior", async () => {
    mockLoadLocalDeviceProfile.mockResolvedValue({
      vehicleName: "Nombre Original",
      vehicleType: "motorcycle",
    });
    mockUpdateDeviceProfile.mockRejectedValue(new TelemetryApiError(400, "protocol", "bad type"));

    await expect(
      updateVehicleProfile(DEVICE_ID, { vehicleName: "Nuevo", vehicleType: "truck" }),
    ).rejects.toMatchObject({ status: 400 });

    expect(mockSaveCachedVehicleName).toHaveBeenCalledWith("Nombre Original");
    expect(mockSaveCachedVehicleType).toHaveBeenCalledWith("motorcycle");
  });

  it("rename con 404 registra y reintenta una vez", async () => {
    mockLoadCachedVehicleName.mockResolvedValue("Anterior");
    mockLoadCachedVehicleType.mockResolvedValue("car");
    mockLoadLocalDeviceProfile.mockResolvedValue({
      vehicleName: "Anterior",
      vehicleType: "car",
    });
    mockUpdateDeviceProfile
      .mockRejectedValueOnce(new TelemetryApiError(404, "protocol", "not found"))
      .mockResolvedValueOnce({
        deviceId: DEVICE_ID,
        vehicleName: "Camión Pereira",
        vehicleType: "car",
      });

    const profile = await updateVehicleDisplayName(DEVICE_ID, "Camión Pereira");

    expect(mockRegisterDevice).toHaveBeenCalledTimes(1);
    expect(mockUpdateDeviceProfile).toHaveBeenCalledTimes(2);
    expect(profile.vehicleName).toBe("Camión Pereira");
    expect(profile.deviceId).toBe(DEVICE_ID);
  });

  it("409 no modifica el nombre local", async () => {
    mockLoadLocalDeviceProfile.mockResolvedValue({
      vehicleName: "Nombre Original",
      vehicleType: "car",
    });
    mockUpdateDeviceProfile.mockRejectedValue(new TelemetryApiError(409, "protocol", "conflict"));

    await expect(updateVehicleDisplayName(DEVICE_ID, "Duplicado")).rejects.toMatchObject({
      status: 409,
    });
    expect(mockSaveCachedVehicleName).toHaveBeenCalledWith("Nombre Original");
  });
});
