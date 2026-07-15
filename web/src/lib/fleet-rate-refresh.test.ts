import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { ApiError } from "@/lib/api-client";
import { resolveFleetFetchError } from "@/lib/fleet-fetch-error";
import {
  applyLocalConnectivity,
  resolveLocalConnectivityStatus,
} from "@/lib/local-connectivity";

describe("resolveFleetFetchError", () => {
  it("distingue 429 de red", () => {
    expect(resolveFleetFetchError(new ApiError("límite", 429, 60))).toContain("limitó temporalmente");
    expect(resolveFleetFetchError(new TypeError("Failed to fetch"))).toContain("puerto 5000");
    expect(resolveFleetFetchError(new ApiError("boom", 503))).toContain("503");
  });
});

describe("local-connectivity", () => {
  it("marca offline cuando lastSeenAt es antiguo", () => {
    const now = Date.parse("2026-07-15T12:00:00Z");
    expect(resolveLocalConnectivityStatus("2026-07-15T11:00:00Z", now, 300)).toBe("offline");
    expect(resolveLocalConnectivityStatus("2026-07-15T11:59:00Z", now, 300)).toBe("online");
  });

  it("aplica estado local sin mutar IDs", () => {
    const now = Date.parse("2026-07-15T12:00:00Z");
    const vehicles = applyLocalConnectivity(
      [
        {
          vehicleId: "VH-001",
          name: "VH-001",
          status: "online",
          lastSeenAt: "2026-07-15T10:00:00Z",
          lastSpeedKmh: 10,
          lastLatitude: 1,
          lastLongitude: 1,
        },
      ],
      now,
    );
    expect(vehicles[0].status).toBe("offline");
    expect(vehicles[0].vehicleId).toBe("VH-001");
  });
});

describe("dashboard sin polling REST agresivo", () => {
  it("page solo usa setInterval para connectivityNowMs", () => {
    const source = readFileSync(resolve(process.cwd(), "src/app/page.tsx"), "utf8");
    expect(source).toContain("setConnectivityNowMs");
    expect(source).toMatch(/setInterval\(\s*\(\)\s*=>\s*setConnectivityNowMs/);
    expect(source).not.toMatch(/setInterval\([\s\S]{0,200}refresh\(/);
    expect(source).not.toMatch(/setInterval\([\s\S]{0,200}loadFromApi\(/);
    expect(source).not.toMatch(/setInterval\([\s\S]{0,200}fetchFleetLive/);
  });
});
