import {
  isDeviceTelemetrySyncEligible,
  parseJwtClaims,
} from "@/services/jwt-claims";

function makeJwt(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.sig`;
}

describe("jwt-claims", () => {
  it("parsea token de dispositivo", () => {
    const claims = parseJwtClaims(makeJwt({
      device_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      role: "device",
      permission: "telemetry:write",
    }));
    expect(claims.sessionKind).toBe("device");
    expect(claims.deviceId).toBe("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    expect(claims.permissions).toEqual(["telemetry:write"]);
  });

  it("parsea token de operador", () => {
    const claims = parseJwtClaims(makeJwt({
      role: "operator",
      permission: ["fleet:read", "device:manage"],
    }));
    expect(claims.sessionKind).toBe("operator");
    expect(claims.deviceId).toBeNull();
    expect(claims.permissions).toContain("device:manage");
  });

  it("isDeviceTelemetrySyncEligible exige device_id coincidente", () => {
    const claims = parseJwtClaims(makeJwt({
      device_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      role: "device",
      permission: "telemetry:write",
    }));
    expect(isDeviceTelemetrySyncEligible(claims, "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")).toBe(true);
    expect(isDeviceTelemetrySyncEligible(claims, "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")).toBe(false);
  });
});
