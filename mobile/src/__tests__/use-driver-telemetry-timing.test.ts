import React from "react";
import TestRenderer, { act } from "react-test-renderer";

const mockSyncPendingQueue = jest.fn();
const mockEnqueueEvent = jest.fn();
const mockCountPendingEvents = jest.fn(async () => 0);
const mockGetCurrentReading = jest.fn();
const mockRunCaptureLoop = jest.fn(async () => undefined);
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

jest.mock("@/services/location-provider", () => ({
  getCurrentReading: () => mockGetCurrentReading(),
  runCaptureLoop: (
    onReading: (reading: unknown) => void | Promise<void>,
    intervalMs: number,
    shouldContinue: () => boolean,
  ) => mockRunCaptureLoop(onReading, intervalMs, shouldContinue),
}));

jest.mock("@/utils/id", () => ({
  generateEventId: jest.fn(async () => `event-${Math.random().toString(16).slice(2)}`),
}));

import { useDriverTelemetry } from "@/hooks/use-driver-telemetry";

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

describe("captura desacoplada de sincronización lenta", () => {
  let latest: HookApi | null = null;
  let renderer: TestRenderer.ReactTestRenderer | null = null;

  beforeEach(() => {
    jest.useFakeTimers();
    mockIsOnline = true;
    latest = null;
    renderer = null;
    syncInFlightCount = 0;
    syncMaxInFlight = 0;
    jest.clearAllMocks();
    mockEnqueueEvent.mockResolvedValue(undefined);
    mockCountPendingEvents.mockResolvedValue(0);
    mockGetCurrentReading.mockResolvedValue({
      latitude: 4.65,
      longitude: -74.08,
      speedKmh: 30,
      source: "simulated",
    });
    mockSyncPendingQueue.mockResolvedValue({ ...SYNC_OK });
  });

  afterEach(() => {
    renderer?.unmount();
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

  it("5 capturas en 20s durante sync lenta sin bloquear", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });

    mockSyncPendingQueue.mockImplementation(async () => {
      syncInFlightCount += 1;
      syncMaxInFlight = Math.max(syncMaxInFlight, syncInFlightCount);
      await gate;
      syncInFlightCount -= 1;
      return { ...SYNC_OK };
    });

    await mount();
    // Liberar sync de montaje (resume)
    await act(async () => {
      release();
      await Promise.resolve();
      await Promise.resolve();
    });

    let releaseSlow!: () => void;
    const slowGate = new Promise<void>((resolve) => {
      releaseSlow = resolve;
    });
    mockSyncPendingQueue.mockImplementation(async () => {
      syncInFlightCount += 1;
      syncMaxInFlight = Math.max(syncMaxInFlight, syncInFlightCount);
      await slowGate;
      syncInFlightCount -= 1;
      return { ...SYNC_OK };
    });
    mockSyncPendingQueue.mockClear();
    mockEnqueueEvent.mockClear();
    syncMaxInFlight = 0;

    // Primera captura dispara sync lenta (no await)
    await act(async () => {
      await latest!.captureAndQueue();
    });
    expect(mockEnqueueEvent).toHaveBeenCalledTimes(1);
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);

    // Cuatro capturas más mientras la sync sigue abierta
    for (let i = 0; i < 4; i += 1) {
      await act(async () => {
        await latest!.captureAndQueue();
      });
    }

    expect(mockEnqueueEvent).toHaveBeenCalledTimes(5);
    expect(syncMaxInFlight).toBe(1);

    await act(async () => {
      releaseSlow();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue.mock.calls.length).toBeGreaterThanOrEqual(2);
    expect(syncMaxInFlight).toBe(1);
  });

  it("excepción inesperada de sync reporta status failed", async () => {
    jest.useRealTimers();
    mockSyncPendingQueue.mockResolvedValue({ ...SYNC_OK });
    await mount();
    mockSyncPendingQueue.mockImplementation(async () => {
      throw new Error("boom de red");
    });

    const result = await latest!.syncNow();

    expect(result.status).toBe("failed");
    expect(result.status).not.toBe("completed");
    expect(latest!.error).toContain("boom de red");
  });
});

