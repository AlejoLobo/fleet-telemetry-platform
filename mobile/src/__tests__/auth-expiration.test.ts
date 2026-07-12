import { parseExpiration } from "@/services/auth-expiration";

describe("auth-expiration", () => {
  const now = Date.parse("2026-07-10T12:00:00Z");

  it("parseExpiration_rechaza_null", () => {
    expect(parseExpiration(null, now)).toEqual({ valid: false, reason: "missing" });
  });

  it("parseExpiration_rechaza_vacio", () => {
    expect(parseExpiration("   ", now)).toEqual({ valid: false, reason: "empty" });
  });

  it("parseExpiration_rechaza_invalido", () => {
    expect(parseExpiration("not-a-date", now)).toEqual({ valid: false, reason: "invalid" });
  });

  it("parseExpiration_rechaza_vencido", () => {
    expect(parseExpiration("2026-07-10T11:00:00Z", now)).toEqual({ valid: false, reason: "expired" });
  });

  it("parseExpiration_acepta_futuro", () => {
    const result = parseExpiration("2026-07-10T13:00:00Z", now);
    expect(result.valid).toBe(true);
    if (result.valid) {
      expect(result.expiresAtMs).toBe(Date.parse("2026-07-10T13:00:00Z"));
    }
  });
});
