import { describe, expect, it, vi } from "vitest";
import { parseAlertPayload, parseFleetUpdatePayload } from "@/lib/sse-parser";

describe("sse-parser", () => {
  it("parsea fleet-update válido", () => {
    const payload = JSON.stringify([
      { vehicleId: "VH-001", name: "VH-001", status: "online", lastSeenAt: null, lastSpeedKmh: 10, lastLatitude: 1, lastLongitude: 2 },
    ]);
    const result = parseFleetUpdatePayload(payload);
    expect(result).toHaveLength(1);
    expect(result?.[0].vehicleId).toBe("VH-001");
  });

  it("acepta flota vacía sin romper el stream", () => {
    expect(parseFleetUpdatePayload("[]")).toEqual([]);
  });

  it("retorna null con JSON inválido", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    expect(parseFleetUpdatePayload("{bad")).toBeNull();
    warn.mockRestore();
  });

  it("parsea alerta válida", () => {
    const alert = {
      alertId: "AL-1",
      vehicleId: "VH-001",
      alertType: "speed",
      severity: "high",
      message: "msg",
      createdAt: "2026-07-10T10:00:00Z",
      isAcknowledged: false,
    };
    expect(parseAlertPayload(JSON.stringify(alert))?.alertId).toBe("AL-1");
  });
});
