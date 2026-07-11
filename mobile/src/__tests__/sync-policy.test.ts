import { TelemetryApiError } from "@/services/telemetry-api";
import { computeBackoffMs, isPermanentSyncError, isTransientSyncError } from "@/services/sync-policy";

describe("sync-policy", () => {
  it("clasifica 400 como fallo permanente", () => {
    expect(isPermanentSyncError(new TelemetryApiError(400, "bad request"))).toBe(true);
  });

  it("clasifica 500 como transitorio", () => {
    expect(isTransientSyncError(new TelemetryApiError(500, "server"))).toBe(true);
    expect(isPermanentSyncError(new TelemetryApiError(500, "server"))).toBe(false);
  });

  it("clasifica 404 como fallo permanente", () => {
    expect(isPermanentSyncError(new TelemetryApiError(404, "not found"))).toBe(true);
  });

  it("clasifica 408 como transitorio", () => {
    expect(isTransientSyncError(new TelemetryApiError(408, "timeout"))).toBe(true);
  });

  it("respeta Retry-After en backoff", () => {
    const delay = computeBackoffMs(2, 15);
    expect(delay).toBe(15_000);
  });
});
