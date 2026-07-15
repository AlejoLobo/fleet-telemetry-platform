const mockRegisterDevice = jest.fn();
const mockRenameDevice = jest.fn();
const mockLoadCachedVehicleName = jest.fn();
const mockMarkDeviceRegistered = jest.fn();
const mockSaveCachedVehicleName = jest.fn();

jest.mock("@/services/device-api", () => ({
  registerDevice: (...args: unknown[]) => mockRegisterDevice(...args),
  renameDevice: (...args: unknown[]) => mockRenameDevice(...args),
}));

jest.mock("@/services/device-profile-store", () => ({
  loadCachedVehicleName: () => mockLoadCachedVehicleName(),
  markDeviceRegistered: (...args: unknown[]) => mockMarkDeviceRegistered(...args),
  saveCachedVehicleName: (...args: unknown[]) => mockSaveCachedVehicleName(...args),
}));

import { TelemetryApiError } from "@/services/telemetry-api";
import {
  ensureDeviceRegistered,
  resetDeviceRegistryForTests,
  updateVehicleDisplayName,
} from "@/services/device-registry";

const DEVICE_ID = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee";

describe("device-registry", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    resetDeviceRegistryForTests();
    mockLoadCachedVehicleName.mockResolvedValue(null);
    mockRegisterDevice.mockResolvedValue({ deviceId: DEVICE_ID, vehicleName: "VH-007" });
    mockRenameDevice.mockResolvedValue({ deviceId: DEVICE_ID, vehicleName: "Unidad Sur" });
    mockMarkDeviceRegistered.mockResolvedValue(undefined);
    mockSaveCachedVehicleName.mockResolvedValue(undefined);
  });

  it("caché existente y servidor disponible: confirma registro", async () => {
    mockLoadCachedVehicleName.mockResolvedValue("VH-OLD");
    const profile = await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledWith(DEVICE_ID);
    expect(profile.vehicleName).toBe("VH-007");
    expect(mockMarkDeviceRegistered).toHaveBeenCalledWith(DEVICE_ID, "VH-007");
    expect(mockSaveCachedVehicleName).not.toHaveBeenCalled();
  });

  it("backend reiniciado: vuelve a registrar", async () => {
    await ensureDeviceRegistered(DEVICE_ID);
    await ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledTimes(2);
  });

  it("dos llamadas concurrentes producen un solo POST", async () => {
    let resolveRegister!: (value: { deviceId: string; vehicleName: string }) => void;
    mockRegisterDevice.mockImplementation(
      () => new Promise((resolve) => {
        resolveRegister = resolve;
      }),
    );

    const p1 = ensureDeviceRegistered(DEVICE_ID);
    const p2 = ensureDeviceRegistered(DEVICE_ID);
    expect(mockRegisterDevice).toHaveBeenCalledTimes(1);
    resolveRegister({ deviceId: DEVICE_ID, vehicleName: "VH-001" });
    const [a, b] = await Promise.all([p1, p2]);
    expect(a.vehicleName).toBe("VH-001");
    expect(b.vehicleName).toBe("VH-001");
    expect(mockRegisterDevice).toHaveBeenCalledTimes(1);
  });

  it("nombre remoto reemplaza caché antigua", async () => {
    mockLoadCachedVehicleName.mockResolvedValue("Nombre Viejo");
    mockRegisterDevice.mockResolvedValue({ deviceId: DEVICE_ID, vehicleName: "VH-042" });
    await ensureDeviceRegistered(DEVICE_ID);
    expect(mockMarkDeviceRegistered).toHaveBeenCalledWith(DEVICE_ID, "VH-042");
    expect(mockSaveCachedVehicleName).not.toHaveBeenCalled();
  });

  it("rename con 404 registra y reintenta una vez", async () => {
    mockLoadCachedVehicleName.mockResolvedValue("Anterior");
    mockRenameDevice
      .mockRejectedValueOnce(new TelemetryApiError(404, "protocol", "not found"))
      .mockResolvedValueOnce({ deviceId: DEVICE_ID, vehicleName: "Camión Pereira" });

    const profile = await updateVehicleDisplayName(DEVICE_ID, "Camión Pereira");

    expect(mockRegisterDevice).toHaveBeenCalledTimes(1);
    expect(mockRenameDevice).toHaveBeenCalledTimes(2);
    expect(profile.vehicleName).toBe("Camión Pereira");
  });

  it("409 no modifica el nombre local", async () => {
    mockLoadCachedVehicleName.mockResolvedValue("Nombre Original");
    mockRenameDevice.mockRejectedValue(new TelemetryApiError(409, "protocol", "conflict"));

    await expect(updateVehicleDisplayName(DEVICE_ID, "Duplicado")).rejects.toMatchObject({
      status: 409,
    });
    expect(mockSaveCachedVehicleName).toHaveBeenCalledWith("Nombre Original");
  });
});
