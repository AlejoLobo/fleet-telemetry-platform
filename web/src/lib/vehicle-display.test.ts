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

  it("en demo usa deviceId explícito para la línea ID", () => {
    expect(
      getVehicleDeviceId({
        vehicleId: "VH-004",
        deviceId: "df32fdsf-43gf-fr32-f34f-4aaaaaaa0001",
      }),
    ).toBe("df32fdsf-43gf-fr32-f34f-4aaaaaaa0001");
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
