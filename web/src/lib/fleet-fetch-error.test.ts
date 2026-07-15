import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-client";

vi.mock("@/lib/utils", () => ({
  getApiBaseUrl: () => "http://localhost:5000",
}));

import { resolveFleetFetchError } from "@/lib/fleet-fetch-error";

describe("resolveFleetFetchError", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("distingue 429 y otros ApiError", () => {
    expect(resolveFleetFetchError(new ApiError("Demasiadas solicitudes", 429))).toContain("429");
    expect(resolveFleetFetchError(new ApiError("fallo interno", 500))).toContain("500");
  });

  it("marca fallo de red/CORS con host de la API", () => {
    const message = resolveFleetFetchError(new TypeError("Failed to fetch"));
    expect(message).toContain("localhost:5000");
    expect(message).toContain("CORS");
  });
});
