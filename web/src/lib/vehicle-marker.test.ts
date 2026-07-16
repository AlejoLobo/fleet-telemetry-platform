/** @vitest-environment jsdom */
import { describe, expect, it } from "vitest";
import type { VehicleStatus } from "@/types/fleet";
import { createVehicleMarkerIcon, escapeHtml } from "@/lib/vehicle-marker";

function baseVehicle(overrides: Partial<VehicleStatus> = {}): VehicleStatus {
  return {
    deviceId: "00000000-0000-4000-8000-000000000001",
    vehicleName: "VH-001",
    vehicleType: "car",
    status: "online",
    lastSeenAt: "2026-07-10T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
    ...overrides,
  };
}

describe("escapeHtml", () => {
  it("escapa caracteres especiales", () => {
    expect(escapeHtml(`a<b>&"c'`)).toBe("a&lt;b&gt;&amp;&quot;c&#39;");
  });
});

describe("createVehicleMarkerIcon", () => {
  const types = ["car", "motorcycle", "van", "truck", "bus", "pickup"] as const;

  it.each(types)("genera SVG distinto para %s", (vehicleType) => {
    const icon = createVehicleMarkerIcon(baseVehicle({ vehicleType }), false);
    expect(icon.options.html).toContain("<svg");
    expect(icon.options.html).toContain(`data-vehicle-type="${vehicleType}"`);
    expect(icon.options.className).toBe("fleet-vehicle-marker");
    expect(icon.options.iconSize).toEqual([48, 62]);
    expect(icon.options.iconAnchor).toEqual([24, 30]);
  });

  it("moto y camión producen siluetas distintas", () => {
    const moto = String(createVehicleMarkerIcon(baseVehicle({ vehicleType: "motorcycle" }), false).options.html);
    const truck = String(createVehicleMarkerIcon(baseVehicle({ vehicleType: "truck" }), false).options.html);
    expect(moto).toContain('data-vehicle-type="motorcycle"');
    expect(truck).toContain('data-vehicle-type="truck"');
    expect(moto).not.toEqual(truck);
  });

  it("etiqueta solo muestra vehicleName escapado", () => {
    const icon = createVehicleMarkerIcon(
      baseVehicle({ vehicleName: `<script>alert("x")</script>` }),
      false,
    );
    expect(icon.options.html).toContain("&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;");
    expect(icon.options.html).not.toContain("<script>");
  });

  it("etiqueta fallback Vehículo sin nombre", () => {
    const icon = createVehicleMarkerIcon(baseVehicle({ vehicleName: "  " }), false);
    expect(icon.options.html).toContain(">Vehículo<");
  });

  it("offline usa color gris", () => {
    const icon = createVehicleMarkerIcon(baseVehicle({ status: "offline" }), false);
    expect(icon.options.html).toContain("#9ca3af");
    expect(icon.options.html).not.toContain("#22c55e");
  });

  it("online usa color verde", () => {
    const icon = createVehicleMarkerIcon(baseVehicle({ status: "online" }), false);
    expect(icon.options.html).toContain("#22c55e");
  });

  it("seleccionado aplica resplandor azul", () => {
    const icon = createVehicleMarkerIcon(baseVehicle(), true);
    expect(icon.options.html).toContain("drop-shadow(0 0 6px #38bdf8)");
  });

  it("aplica rotación por headingDegrees", () => {
    const icon = createVehicleMarkerIcon(baseVehicle({ headingDegrees: 90 }), false);
    expect(icon.options.html).toContain("rotate(90deg)");
  });
});
