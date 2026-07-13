import { describe, expect, it } from "vitest";
import {
  DECIMAL_EVENT_ID_PATTERN,
  parseDecimalEventId,
  parseStreamResetPayload,
  ResyncFailedError,
} from "@/lib/sse-resync";

describe("sse-resync", () => {
  it("parsea_latestEventId_como_cadena_decimal", () => {
    const parsed = parseStreamResetPayload(JSON.stringify({
      reason: "replay-gap",
      latestEventId: "100",
    }));
    expect(parsed).toEqual({ reason: "replay-gap", latestEventId: "100" });
  });

  it("preserva_offsets_de_64_bits_sin_conversion_numerica", () => {
    const largeId = "9007199254740993";
    const nearMax = "9223372036854775806";
    expect(parseStreamResetPayload(JSON.stringify({
      reason: "replay-gap",
      latestEventId: largeId,
    }))).toEqual({ reason: "replay-gap", latestEventId: largeId });
    expect(parseStreamResetPayload(JSON.stringify({
      reason: "replay-gap",
      latestEventId: nearMax,
    }))).toEqual({ reason: "replay-gap", latestEventId: nearMax });
    expect(parseDecimalEventId(largeId)).toBe(largeId);
    expect(DECIMAL_EVENT_ID_PATTERN.test(largeId)).toBe(true);
  });

  it("rechaza_latestEventId_numerico_en_json", () => {
    expect(parseStreamResetPayload(JSON.stringify({
      reason: "replay-gap",
      latestEventId: 100,
    }))).toBeNull();
  });

  it("latestEventId_null_es_valido", () => {
    const parsed = parseStreamResetPayload(JSON.stringify({
      reason: "instance-restarted",
      latestEventId: null,
    }));
    expect(parsed).toEqual({ reason: "instance-restarted", latestEventId: null });
  });

  it("payload_invalido_retorna_null", () => {
    expect(parseStreamResetPayload("not-json")).toBeNull();
    expect(parseStreamResetPayload(JSON.stringify({ latestEventId: "1" }))).toBeNull();
  });

  it("ResyncFailedError_expone_nombre", () => {
    const error = new ResyncFailedError("fallo");
    expect(error.name).toBe("ResyncFailedError");
    expect(error.message).toBe("fallo");
  });
});
