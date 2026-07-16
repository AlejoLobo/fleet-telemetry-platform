/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("fleet-data modo E2E determinista", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.unstubAllEnvs();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("misma semilla y secuencia producen el mismo dataset", async () => {
    vi.stubEnv("NEXT_PUBLIC_E2E_TEST_MODE", "true");
    vi.stubEnv("NEXT_PUBLIC_E2E_SEED", "12345");
    const mod = await import("@/mocks/fleet-data");
    mod.resetMockDatasetForTests();
    const a = mod.refreshMockDataset(10);
    mod.resetMockDatasetForTests();
    const b = mod.refreshMockDataset(10);
    expect(a.vehicles[0]?.lastSpeedKmh).toBe(b.vehicles[0]?.lastSpeedKmh);
    expect(a.vehicles[0]?.deviceId).toBe(b.vehicles[0]?.deviceId);
    expect(a.vehicles[0]?.deviceId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
  });

  it("secuencias diferentes producen velocidades distintas en E2E", async () => {
    vi.stubEnv("NEXT_PUBLIC_E2E_TEST_MODE", "true");
    vi.stubEnv("NEXT_PUBLIC_E2E_SEED", "12345");
    const mod = await import("@/mocks/fleet-data");
    mod.resetMockDatasetForTests();
    const first = mod.refreshMockDataset(10);
    const second = mod.refreshMockDataset(10);
    expect(first.vehicles[0]?.lastSpeedKmh).toBe(25); // 20 + 1*5
    expect(second.vehicles[0]?.lastSpeedKmh).toBe(30); // 20 + 2*5
    expect(first.vehicles[0]?.lastSpeedKmh).not.toBe(second.vehicles[0]?.lastSpeedKmh);
  });

  it("modo normal sigue generando sin requerir E2E", async () => {
    vi.stubEnv("NEXT_PUBLIC_E2E_TEST_MODE", "false");
    const mod = await import("@/mocks/fleet-data");
    mod.resetMockDatasetForTests();
    const dataset = mod.refreshMockDataset(8);
    expect(dataset.vehicles.length).toBeGreaterThanOrEqual(8);
    expect(dataset.vehicles[0]?.deviceId).toMatch(/^00000000-0000-4000-8000-/);
  });

  it("resetMockDatasetForTests limpia secuencia y caché", async () => {
    vi.stubEnv("NEXT_PUBLIC_E2E_TEST_MODE", "true");
    vi.stubEnv("NEXT_PUBLIC_E2E_SEED", "99");
    const mod = await import("@/mocks/fleet-data");
    mod.resetMockDatasetForTests();
    mod.refreshMockDataset(5);
    expect(mod.getDemoRefreshSequenceForTests()).toBe(1);
    mod.resetMockDatasetForTests();
    expect(mod.getDemoRefreshSequenceForTests()).toBe(0);
  });
});
