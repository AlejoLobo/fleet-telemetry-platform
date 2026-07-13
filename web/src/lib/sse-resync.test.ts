import { describe, expect, it } from "vitest";
import { parseStreamResetPayload, ResyncFailedError } from "@/lib/sse-resync";

describe("sse-resync", () => {
  it("parsea_latestEventId_del_payload_stream_reset", () => {
    const parsed = parseStreamResetPayload(JSON.stringify({
      reason: "replay-gap",
      latestEventId: 100,
    }));
    expect(parsed).toEqual({ reason: "replay-gap", latestEventId: 100 });
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
    expect(parseStreamResetPayload(JSON.stringify({ latestEventId: 1 }))).toBeNull();
  });

  it("ResyncFailedError_expone_nombre", () => {
    const error = new ResyncFailedError("fallo");
    expect(error.name).toBe("ResyncFailedError");
    expect(error.message).toBe("fallo");
  });
});
