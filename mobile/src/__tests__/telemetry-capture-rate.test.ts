import {
  DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS,
  isTelemetryCaptureIntervalSeconds,
  parseTelemetryCaptureIntervalSeconds,
  TELEMETRY_CAPTURE_INTERVAL_OPTIONS_SECONDS,
} from "@/config/telemetry-capture-rate";

describe("telemetry-capture-rate", () => {
  it("acepta solo 3, 5, 10 y 15", () => {
    expect(TELEMETRY_CAPTURE_INTERVAL_OPTIONS_SECONDS).toEqual([3, 5, 10, 15]);
    for (const value of [3, 5, 10, 15]) {
      expect(isTelemetryCaptureIntervalSeconds(value)).toBe(true);
    }
    expect(isTelemetryCaptureIntervalSeconds(8)).toBe(false);
    expect(isTelemetryCaptureIntervalSeconds(4)).toBe(false);
    expect(isTelemetryCaptureIntervalSeconds("5")).toBe(false);
  });

  it("parse inválido usa 5 segundos", () => {
    expect(parseTelemetryCaptureIntervalSeconds(undefined)).toBe(DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS);
    expect(parseTelemetryCaptureIntervalSeconds("7")).toBe(5);
    expect(parseTelemetryCaptureIntervalSeconds(NaN)).toBe(5);
    expect(parseTelemetryCaptureIntervalSeconds("15")).toBe(15);
  });
});
