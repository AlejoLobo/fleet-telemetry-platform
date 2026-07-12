/** @vitest-environment jsdom */
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useFleetData } from "@/hooks/use-fleet-data";

const fetchFleetLive = vi.fn();
const fetchAlertsLive = vi.fn();
const fetchOpsSummary = vi.fn();

vi.mock("@/lib/api-client", () => ({
  apiClient: {
    fetchFleetLive: (...args: unknown[]) => fetchFleetLive(...args),
    fetchAlertsLive: (...args: unknown[]) => fetchAlertsLive(...args),
    fetchOpsSummary: (...args: unknown[]) => fetchOpsSummary(...args),
  },
}));

vi.mock("@/lib/fleet-pagination", () => ({
  fetchTelemetrySnapshot: vi.fn(async () => ({
    events: [],
    partial: false,
    truncated: false,
  })),
}));

vi.mock("@/lib/utils", () => ({
  getApiBaseUrl: () => "http://localhost:5000",
}));

describe("useFleetData truncated snapshot", () => {
  beforeEach(() => {
    fetchFleetLive.mockReset();
    fetchAlertsLive.mockReset();
    fetchOpsSummary.mockReset();
  });

  it("useFleetData_muestra_snapshot_truncado", async () => {
    fetchFleetLive.mockResolvedValue({
      vehicles: [{ vehicleId: "VH-001", name: "VH-001", status: "online", lastSeenAt: "2026-07-10T10:00:00Z", lastSpeedKmh: 1, lastLatitude: 1, lastLongitude: 1 }],
      partial: true,
      truncated: true,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchOpsSummary.mockResolvedValue({ totalVehicles: 9000, activeVehicles: 6000, criticalAlerts: 3 });

    const { result } = renderHook(() => useFleetData("VH-001"));

    await waitFor(() => expect(result.current.fleetLoading).toBe(false));

    expect(result.current.fleetTruncated).toBe(true);
    expect(result.current.fleetError).toContain("Snapshot parcial");
    expect(result.current.globalAnalytics.totalVehicles).toBe(9000);
    expect(result.current.globalAnalytics.activeVehicles).toBe(6000);
    expect(result.current.globalAnalytics.partial).toBe(true);
  });
});
