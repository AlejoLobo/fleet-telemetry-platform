import { describe, expect, it } from "vitest";
import { parseFleetUpdatePayload, parseVehicleUpdatePayload } from "@/lib/sse-parser";
import { SseParser } from "@/lib/sse-fetch-client";
import { REALTIME_EVENTS } from "@/lib/realtime-events";

describe("sse parser canonical contract", () => {
  it("parsea_vehicle_update_individual", () => {
    const payload = JSON.stringify({
      deviceId: "00000000-0000-4000-8000-000000000001",
      vehicleName: "00000000-0000-4000-8000-000000000001",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 45,
      lastLatitude: 4.6,
      lastLongitude: -74.0,
    });

    const vehicle = parseVehicleUpdatePayload(payload);
    expect(vehicle?.deviceId).toBe("00000000-0000-4000-8000-000000000001");
    expect(vehicle?.lastSpeedKmh).toBe(45);
    expect(vehicle?.vehicleType).toBe("car");
  });

  it("parsea_vehicleType_desde_payload", () => {
    const payload = JSON.stringify({
      deviceId: "00000000-0000-4000-8000-000000000001",
      vehicleType: "TRUCK",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
    });

    const vehicle = parseVehicleUpdatePayload(payload);
    expect(vehicle?.vehicleType).toBe("truck");
    expect(vehicle?.vehicleTypeFromPayload).toBe(true);
  });

  it("parsea_fleet_update_array_legacy", () => {
    const payload = JSON.stringify([
      { deviceId: "00000000-0000-4000-8000-000000000001", status: "online", lastSeenAt: "2026-07-10T10:00:00Z" },
      { deviceId: "00000000-0000-4000-8000-000000000002", status: "offline", lastSeenAt: "2026-07-10T09:00:00Z" },
    ]);

    const vehicles = parseFleetUpdatePayload(payload);
    expect(vehicles).toHaveLength(2);
  });

  it("contrato_canonico_usa_vehicle_update", () => {
    expect(REALTIME_EVENTS.vehicleUpdate).toBe("vehicle-update");
    expect(REALTIME_EVENTS.fleetUpdate).toBe("fleet-update");
  });
});

describe("SseParser FT-005", () => {
  it("Parser_extrae_id_SSE", () => {
    const parser = new SseParser();
    const events = parser.feed("id: 77\nevent: vehicle-update\ndata: {}\n\n");
    expect(events[0]?.id).toBe("77");
  });

  it("Parser_soporta_id_fragmentado_entre_chunks", () => {
    const parser = new SseParser();
    parser.feed("id: 10");
    const events = parser.feed("05\nevent: alert\ndata: {}\n\n");
    expect(events[0]?.id).toBe("1005");
  });
});
