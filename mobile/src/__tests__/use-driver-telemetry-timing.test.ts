/**
 * Valida el bucle real de captura cada 5 segundos con fake timers.
 * Conserva el coordinador mockeado (API remota) pero usa runCaptureLoop real.
 */
import React from "react";
import TestRenderer, { act } from "react-test-renderer";

const mockSyncPendingQueue = jest.fn();
const mockEnqueueEvent = jest.fn();
const mockCountPendingEvents = jest.fn(async () => 0);
const mockGetCurrentReading = jest.fn();

let mockIsOnline = true;
let syncInFlightCount = 0;
let syncMaxInFlight = 0;

jest.mock("@/hooks/use-network-status", () => ({
  useNetworkStatus: () => ({
    isOnline: mockIsOnline,
    status: mockIsOnline ? "online" : "offline",
  }),
}));

jest.mock("@/services/offline-sync-coordinator", () => {
  let inFlight: Promise<unknown> | null = null;
  let requested = false;
  return {
    syncPendingQueue: (...args: unknown[]) => {
      if (inFlight) {
        requested = true;
        return inFlight;
      }
      const run = async () => {
        let last = await mockSyncPendingQueue(...args);
        while (requested) {
          requested = false;
          last = await mockSyncPendingQueue(...args);
        }
        return last;
      };
      inFlight = run().finally(() => {
        inFlight = null;
      });
      return inFlight;
    },
    resetSyncCoordinatorForTests: jest.fn(),
  };
});

jest.mock("@/db/offline-queue", () => ({
  enqueueEvent: (...args: Parameters<typeof mockEnqueueEvent>) => mockEnqueueEvent(...args),
  countPendingEvents: () => mockCountPendingEvents(),
  resetOfflineQueueForTests: jest.fn(),
}));

// runCaptureLoop real; getCurrentReading inyectado vía 4º argumento.
jest.mock("@/services/location-provider", () => {
  const actual = jest.requireActual<typeof import("@/services/location-provider")>(
    "@/services/location-provider",
  );
  return {
    getCurrentReading: () => mockGetCurrentReading(),
    runCaptureLoop: (
      onReading: (reading: unknown) => void | Promise<void>,
      intervalMs: number,
      shouldContinue: () => boolean,
    ) =>
      actual.runCaptureLoop(
        onReading as never,
        intervalMs,
        shouldContinue,
        () => mockGetCurrentReading(),
      ),
  };
});

jest.mock("@/utils/id", () => ({
  generateEventId: jest.fn(async () => `event-${Math.random().toString(16).slice(2)}`),
}));

import { useDriverTelemetry } from "@/hooks/use-driver-telemetry";
import { TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS } from "@/config/telemetry-capture-rate";

type HookApi = ReturnType<typeof useDriverTelemetry>;
const DEVICE_ID = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
const SYNC_OK = {
  synced: 1,
  failed: 0,
  retried: 0,
  permanentFailures: 0,
  remaining: 0,
  status: "completed" as const,
};

function Harness({ onReady }: { onReady: (api: HookApi) => void }) {
  const api = useDriverTelemetry(DEVICE_ID, "DRV-001", true);
  onReady(api);
  return null;
}

describe("captura real cada 5 segundos (runCaptureLoop)", () => {
  let latest: HookApi | null = null;
  let renderer: TestRenderer.ReactTestRenderer | null = null;
  const enqueueAtMs: number[] = [];

  beforeEach(() => {
    jest.useFakeTimers({ advanceTimers: true });
    mockIsOnline = true;
    latest = null;
    renderer = null;
    syncInFlightCount = 0;
    syncMaxInFlight = 0;
    enqueueAtMs.length = 0;
    jest.clearAllMocks();
    mockEnqueueEvent.mockImplementation(async () => {
      enqueueAtMs.push(jest.now());
    });
    mockCountPendingEvents.mockResolvedValue(0);
    mockGetCurrentReading.mockResolvedValue({
      latitude: 4.65,
      longitude: -74.08,
      speedKmh: 30,
      source: "simulated",
    });
    mockSyncPendingQueue.mockResolvedValue({ ...SYNC_OK });
  });

  afterEach(async () => {
    await act(async () => {
      if (latest?.tracking) {
        await latest.stopTracking();
      }
      renderer?.unmount();
    });
    renderer = null;
    latest = null;
    jest.clearAllTimers();
    jest.useRealTimers();
  });

  async function mount() {
    await act(async () => {
      renderer = TestRenderer.create(
        React.createElement(Harness, {
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });
    expect(latest).not.toBeNull();
  }

  async function flushMicrotasks(times = 15) {
    await act(async () => {
      for (let i = 0; i < times; i += 1) {
        await Promise.resolve();
      }
    });
  }

  it("capturas en 0, 5, 10, 15 y 20 segundos con el loop real", async () => {
    expect(TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS).toBe(5_000);

    let releaseSlow!: () => void;
    const slowGate = new Promise<void>((resolve) => {
      releaseSlow = resolve;
    });

    mockSyncPendingQueue.mockResolvedValueOnce({ ...SYNC_OK });
    await mount();
    await flushMicrotasks();

    mockSyncPendingQueue.mockImplementation(async () => {
      syncInFlightCount += 1;
      syncMaxInFlight = Math.max(syncMaxInFlight, syncInFlightCount);
      await slowGate;
      syncInFlightCount -= 1;
      return { ...SYNC_OK };
    });
    mockSyncPendingQueue.mockClear();
    enqueueAtMs.length = 0;

    const t0 = jest.now();
    await act(async () => {
      await latest!.startTracking();
    });
    await flushMicrotasks(30);

    // Comportamiento del loop: primera captura inmediata (t≈0).
    expect(enqueueAtMs.length).toBeGreaterThanOrEqual(1);
    expect(enqueueAtMs[0]! - t0).toBeLessThan(50);

    for (const expectedCount of [2, 3, 4, 5]) {
      await act(async () => {
        await jest.advanceTimersByTimeAsync(5_000);
      });
      await flushMicrotasks(20);
      expect(enqueueAtMs).toHaveLength(expectedCount);
      const elapsed = enqueueAtMs[expectedCount - 1]! - t0;
      const target = (expectedCount - 1) * 5_000;
      expect(elapsed).toBeGreaterThanOrEqual(target - 50);
      expect(elapsed).toBeLessThanOrEqual(target + 250);
    }

    expect(enqueueAtMs).toHaveLength(5);
    expect(syncMaxInFlight).toBeLessThanOrEqual(1);

    await act(async () => {
      releaseSlow();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue.mock.calls.length).toBeGreaterThanOrEqual(1);
    expect(syncMaxInFlight).toBe(1);
  });

  it("detener tracking cancela nuevas capturas", async () => {
    await mount();
    await flushMicrotasks();
    enqueueAtMs.length = 0;

    await act(async () => {
      await latest!.startTracking();
    });
    await flushMicrotasks(30);
    const afterStart = enqueueAtMs.length;
    expect(afterStart).toBeGreaterThanOrEqual(1);

    await act(async () => {
      await latest!.stopTracking();
    });
    await flushMicrotasks();

    await act(async () => {
      await jest.advanceTimersByTimeAsync(20_000);
    });
    await flushMicrotasks(10);

    expect(enqueueAtMs.length).toBe(afterStart);
  });

  it("excepción inesperada de sync reporta status failed", async () => {
    jest.useRealTimers();
    mockSyncPendingQueue.mockResolvedValue({ ...SYNC_OK });
    await mount();
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });

    mockSyncPendingQueue.mockImplementation(async () => {
      throw new Error("boom de red");
    });

    let result!: Awaited<ReturnType<HookApi["syncNow"]>>;
    await act(async () => {
      result = await latest!.syncNow();
    });

    expect(result.status).toBe("failed");
    expect(result.status).not.toBe("completed");
    await act(async () => {
      await Promise.resolve();
    });
    expect(latest!.lastSync?.status).toBe("failed");
    expect(latest!.error).toEqual(expect.stringContaining("boom de red"));
  });
});
