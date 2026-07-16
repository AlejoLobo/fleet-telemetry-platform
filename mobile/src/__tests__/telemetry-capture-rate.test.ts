import {
  TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS,
  TELEMETRY_SYNC_INTERVAL_MILLISECONDS,
} from "../config/telemetry-capture-rate";
import * as captureRate from "../config/telemetry-capture-rate";

describe("telemetry-capture-rate", () => {
  it("fija captura y sync de respaldo en 5 segundos", () => {
    expect(TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS).toBe(5_000);
    expect(TELEMETRY_SYNC_INTERVAL_MILLISECONDS).toBe(5_000);
  });

  it("no exporta opciones seleccionables", () => {
    expect(Object.keys(captureRate).sort()).toEqual([
      "TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS",
      "TELEMETRY_SYNC_INTERVAL_MILLISECONDS",
    ]);
  });
});
