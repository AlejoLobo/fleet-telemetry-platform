import { describe, expect, it } from "vitest";
import { computeReconnectDelay, MAX_RECONNECT_MS } from "@/lib/sse-reconnect";

describe("sse-reconnect", () => {
  it("crece exponencialmente y respeta el máximo", () => {
    expect(computeReconnectDelay(0)).toBeGreaterThanOrEqual(1000);
    expect(computeReconnectDelay(10)).toBeLessThanOrEqual(MAX_RECONNECT_MS + 500);
  });
});
