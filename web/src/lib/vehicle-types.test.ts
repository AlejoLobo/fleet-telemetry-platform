import { describe, expect, it } from "vitest";
import { normalizeVehicleType, vehicleTypeLabel } from "@/lib/vehicle-types";

describe("normalizeVehicleType", () => {
  it("acepta tipos válidos", () => {
    expect(normalizeVehicleType("truck")).toBe("truck");
    expect(normalizeVehicleType("motorcycle")).toBe("motorcycle");
    expect(normalizeVehicleType("pickup")).toBe("pickup");
  });

  it("normaliza mayúsculas", () => {
    expect(normalizeVehicleType("TRUCK")).toBe("truck");
    expect(normalizeVehicleType("Van")).toBe("van");
  });

  it("valor ausente o inválido devuelve car", () => {
    expect(normalizeVehicleType(undefined)).toBe("car");
    expect(normalizeVehicleType(null)).toBe("car");
    expect(normalizeVehicleType("")).toBe("car");
    expect(normalizeVehicleType("  ")).toBe("car");
    expect(normalizeVehicleType("helicopter")).toBe("car");
    expect(normalizeVehicleType(42)).toBe("car");
  });
});

describe("vehicleTypeLabel", () => {
  it("devuelve etiquetas en español", () => {
    expect(vehicleTypeLabel("car")).toBe("Automóvil");
    expect(vehicleTypeLabel("motorcycle")).toBe("Motocicleta");
    expect(vehicleTypeLabel("van")).toBe("Van");
    expect(vehicleTypeLabel("truck")).toBe("Camión");
    expect(vehicleTypeLabel("bus")).toBe("Bus");
    expect(vehicleTypeLabel("pickup")).toBe("Camioneta");
  });
});
