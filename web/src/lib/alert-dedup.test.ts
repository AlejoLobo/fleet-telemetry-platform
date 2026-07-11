import { describe, expect, it } from "vitest";
import { dedupeAlerts } from "@/lib/alert-dedup";
import type { FleetAlert } from "@/types/fleet";

const alert = (id: string): FleetAlert => ({
  alertId: id,
  vehicleId: "VH-001",
  alertType: "speed",
  severity: "high",
  message: "test",
  createdAt: "2026-07-10T10:00:00Z",
  isAcknowledged: false,
});

describe("dedupeAlerts", () => {
  it("elimina alertas duplicadas por alertId", () => {
    const result = dedupeAlerts([alert("A1"), alert("A2"), alert("A1")]);
    expect(result.map((a) => a.alertId)).toEqual(["A1", "A2"]);
  });

  it("preserva orden de primera aparición", () => {
    const first = alert("X");
    const second = { ...alert("X"), message: "otra" };
    const result = dedupeAlerts([first, second]);
    expect(result).toHaveLength(1);
    expect(result[0].message).toBe("test");
  });
});
