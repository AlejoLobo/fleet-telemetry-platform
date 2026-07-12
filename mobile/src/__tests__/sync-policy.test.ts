import { classifySyncError, isPermanentSyncError, isTransientSyncError } from "@/services/sync-policy";
import { TelemetryApiError } from "@/services/telemetry-api";

describe("sync-policy", () => {
  it("clasifica_400_como_aislamiento_de_validacion", () => {
    expect(classifySyncError(new TelemetryApiError(400, "validation", "bad")).action).toBe("isolate_validation");
  });

  it("clasifica_401_como_stop_auth_required", () => {
    expect(classifySyncError(new TelemetryApiError(401, "auth_required", "auth")).action).toBe("stop_auth_required");
    expect(isPermanentSyncError(new TelemetryApiError(401, "auth_required", "auth"))).toBe(false);
  });

  it("clasifica_403_como_stop_forbidden", () => {
    expect(classifySyncError(new TelemetryApiError(403, "forbidden", "forbidden")).action).toBe("stop_forbidden");
  });

  it("clasifica_404_como_configuration_error", () => {
    expect(classifySyncError(new TelemetryApiError(404, "protocol", "not found")).action).toBe("stop_configuration");
    expect(isPermanentSyncError(new TelemetryApiError(404, "protocol", "not found"))).toBe(false);
  });

  it("clasifica_500_como_transitorio", () => {
    expect(classifySyncError(new TelemetryApiError(500, "transient", "server")).action).toBe("stop_transient");
    expect(isTransientSyncError(new TelemetryApiError(500, "transient", "server"))).toBe(true);
  });
});
