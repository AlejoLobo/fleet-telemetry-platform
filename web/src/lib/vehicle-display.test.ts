import { describe, expect, it } from "vitest";
import { getVehicleDisplayName } from "@/lib/vehicle-display";

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
});
