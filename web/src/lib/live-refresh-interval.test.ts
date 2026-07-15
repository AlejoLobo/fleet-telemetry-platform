import { describe, expect, it } from "vitest";
import {
  DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS,
  isLiveRefreshIntervalSeconds,
  parseLiveRefreshIntervalSeconds,
} from "@/lib/live-refresh-interval";

describe("live-refresh-interval", () => {
  it("acepta solo 3, 5, 10 y 15 segundos", () => {
    expect(isLiveRefreshIntervalSeconds(3)).toBe(true);
    expect(isLiveRefreshIntervalSeconds(5)).toBe(true);
    expect(isLiveRefreshIntervalSeconds(10)).toBe(true);
    expect(isLiveRefreshIntervalSeconds(15)).toBe(true);
    expect(isLiveRefreshIntervalSeconds(7)).toBe(false);
    expect(isLiveRefreshIntervalSeconds("5")).toBe(false);
  });

  it("parsea valores inválidos al default", () => {
    expect(parseLiveRefreshIntervalSeconds(null)).toBe(DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS);
    expect(parseLiveRefreshIntervalSeconds("abc")).toBe(DEFAULT_LIVE_REFRESH_INTERVAL_SECONDS);
    expect(parseLiveRefreshIntervalSeconds("10")).toBe(10);
  });
});
