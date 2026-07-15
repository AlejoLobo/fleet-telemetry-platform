const mockRegisterDevice = jest.fn();
const mockRenameDevice = jest.fn();
const mockLoadCachedVehicleName = jest.fn();
const mockLoadRegisteredDeviceId = jest.fn();
const mockMarkDeviceRegistered = jest.fn();
const mockSaveCachedVehicleName = jest.fn();

jest.mock("@/services/device-api", () => ({
  registerDevice: (...args: unknown[]) => mockRegisterDevice(...args),
  renameDevice: (...args: unknown[]) => mockRenameDevice(...args),
}));

jest.mock("@/services/device-profile-store", () => ({
  loadCachedVehicleName: () => mockLoadCachedVehicleName(),
  loadRegisteredDeviceId: () => mockLoadRegisteredDeviceId(),
  markDeviceRegistered: (...args: unknown[]) => mockMarkDeviceRegistered(...args),
  saveCachedVehicleName: (...args: unknown[]) => mockSaveCachedVehicleName(...args),
}));

import { ensureDeviceRegistered, updateVehicleDisplayName } from "@/services/device-registry";

const DEVICE_ID = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

describe("device-registry", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockLoadRegisteredDeviceId.mockResolvedValue(null);
    mockLoadCachedVehicleName.mockResolvedValue(null);
    mockRegisterDevice.mockResolvedValue({ deviceId: DEVICE_ID, vehicleName: "VH-007" });
    mockRenameDevice.mockResolvedValue({ deviceId: DEVICE_ID, vehicleName: "Unidad Sur" });
    mockMarkDeviceRegistered.mockResolvedValue(undefined);
    mockSaveCachedVehicleName.mockResolvedValue(undefined);
  });

  it("registra en backend cuando no hay cache local", async () => {
    const profile = await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledWith(DEVICE_ID);
    expect(mockMarkDeviceRegistered).toHaveBeenCalledWith(DEVICE_ID, "VH-007");
    expect(profile.vehicleName).toBe("VH-007");
  });

  it("omite registro remoto si ya está cacheado para el mismo DeviceId", async () => {
    mockLoadRegisteredDeviceId.mockResolvedValue(DEVICE_ID);
    mockLoadCachedVehicleName.mockResolvedValue("VH-007");
    const profile = await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).not.toHaveBeenCalled();
    expect(profile).toEqual({ deviceId: DEVICE_ID, vehicleName: "VH-007" });
  });

  it("vuelve a registrar si el DeviceId cacheado no coincide", async () => {
    mockLoadRegisteredDeviceId.mockResolvedValue("other-id");
    mockLoadCachedVehicleName.mockResolvedValue("VH-001");
    await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledWith(DEVICE_ID);
  });

  it("renombra sin regenerar identidad", async () => {
    mockLoadRegisteredDeviceId.mockResolvedValue(DEVICE_ID);
    mockLoadCachedVehicleName.mockResolvedValue("VH-007");
    const profile = await updateVehicleDisplayName(DEVICE_ID, "Unidad Sur");
    expect(mockRenameDevice).toHaveBeenCalledWith(DEVICE_ID, "Unidad Sur");
    expect(profile.deviceId).toBe(DEVICE_ID);
    expect(profile.vehicleName).toBe("Unidad Sur");
    expect(mockRegisterDevice).not.toHaveBeenCalled();
  });
});
