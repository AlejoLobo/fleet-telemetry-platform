import { describe, expect, it } from "vitest";
import {
  getVehicleDeviceId,
  getVehicleDisplayName,
  isDeviceUuid,
  isMeaningfulVehicleName,
  resolveVehicleName,
} from "@/lib/vehicle-display";

describe("vehicle-display", () => {
  it("usa VH-001 como nombre aunque el deviceId sea un UUID", () => {
    expect(
      getVehicleDisplayName({
        vehicleId: "11111111-1111-1111-1111-111111111111",
        name: "VH-001",
      }),
    ).toBe("VH-001");
  });

  it("no usa un UUID como nombre del vehículo", () => {
    expect(
      getVehicleDisplayName({
        vehicleId: "11111111-1111-1111-1111-111111111111",
        name: "11111111-1111-1111-1111-111111111111",
      }),
    ).toBe("Sin nombre");
  });

  it("en demo alineado a mobile usa deviceId/vehicleId UUID en la línea ID", () => {
    expect(
      getVehicleDeviceId({
        vehicleId: "11111111-1111-1111-1111-111111111111",
        deviceId: "11111111-1111-1111-1111-111111111111",
      }),
    ).toBe("11111111-1111-1111-1111-111111111111");
  });

  it("isMeaningfulVehicleName acepta códigos de flota", () => {
    expect(isMeaningfulVehicleName("VH-001")).toBe(true);
    expect(isMeaningfulVehicleName("Camión norte")).toBe(true);
    expect(isMeaningfulVehicleName("")).toBe(false);
    expect(isDeviceUuid("11111111-1111-1111-1111-111111111111")).toBe(true);
  });

  it("resolveVehicleName prioriza el nombre operativo", () => {
    expect(resolveVehicleName("VH-002", "11111111-1111-1111-1111-111111111111")).toBe("VH-002");
  });
});
