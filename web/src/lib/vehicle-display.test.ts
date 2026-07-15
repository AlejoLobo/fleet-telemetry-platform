import { describe, expect, it } from "vitest";
import {
  getVehicleDisplayName,
  isMeaningfulVehicleName,
  resolveVehicleName,
} from "@/lib/vehicle-display";

describe("vehicle-display", () => {
  it("no usa el id de dispositivo como nombre", () => {
    expect(
      getVehicleDisplayName({
        vehicleId: "11111111-1111-1111-1111-111111111111",
        name: "11111111-1111-1111-1111-111111111111",
      }),
    ).toBe("Sin nombre");
  });

  it("usa el nombre cuando es distinto del id", () => {
    expect(
      getVehicleDisplayName({
        vehicleId: "11111111-1111-1111-1111-111111111111",
        name: "Camión norte",
      }),
    ).toBe("Camión norte");
  });

  it("resolveVehicleName conserva el nombre útil ante un fallback igual al id", () => {
    expect(
      resolveVehicleName(
        "11111111-1111-1111-1111-111111111111",
        "Camión norte",
        "11111111-1111-1111-1111-111111111111",
      ),
    ).toBe("Camión norte");
  });

  it("isMeaningfulVehicleName rechaza vacíos e ids", () => {
    expect(isMeaningfulVehicleName("", "VH-001")).toBe(false);
    expect(isMeaningfulVehicleName("VH-001", "VH-001")).toBe(false);
    expect(isMeaningfulVehicleName("Camión", "VH-001")).toBe(true);
  });
});
