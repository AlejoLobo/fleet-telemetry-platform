import React from "react";
import TestRenderer, { act } from "react-test-renderer";

const mockSyncPendingQueue = jest.fn();
const mockEnqueueEvent = jest.fn();
const mockCountPendingEvents = jest.fn(async () => 0);
const mockGetCurrentReading = jest.fn();
const mockRunCaptureLoop = jest.fn();
let mockIsOnline = true;

jest.mock("@/hooks/use-network-status", () => ({
  useNetworkStatus: () => ({
    isOnline: mockIsOnline,
    status: mockIsOnline ? "online" : "offline",
  }),
}));

jest.mock("@/services/offline-sync-coordinator", () => ({
  syncPendingQueue: (isOnline: boolean) => mockSyncPendingQueue(isOnline),
  resetSyncCoordinatorForTests: jest.fn(),
}));

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
  generateEventId: jest.fn(async () => "generated-event-id"),
}));

import { SYNC_INTERVAL_MILLISECONDS, useDriverTelemetry } from "@/hooks/use-driver-telemetry";

type HookApi = ReturnType<typeof useDriverTelemetry>;

function Harness({
  canSync,
  interval,
  onReady,
}: {
  canSync: boolean;
  interval: 3 | 5 | 10 | 15;
  onReady: (api: HookApi) => void;
}) {
  const api = useDriverTelemetry("VH-001", "DRV-001", canSync, interval);
  React.useEffect(() => {
    onReady(api);
  }, [api, onReady]);
  return null;
}

describe("useDriverTelemetry captura vs sync", () => {
  let latest: HookApi | null = null;

  beforeEach(() => {
    jest.useFakeTimers();
    mockIsOnline = true;
    latest = null;
    jest.clearAllMocks();
    mockSyncPendingQueue.mockResolvedValue({
      synced: 1,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: 0,
      status: "completed",
    });
    mockEnqueueEvent.mockResolvedValue(undefined);
    mockCountPendingEvents.mockResolvedValue(0);
    mockGetCurrentReading.mockResolvedValue({
      latitude: 4.65,
      longitude: -74.08,
      speedKmh: 30,
      source: "simulated",
    });
    mockRunCaptureLoop.mockImplementation(async (onReading, _ms, shouldContinue) => {
      if (shouldContinue()) await onReading({
        latitude: 4.65,
        longitude: -74.08,
        speedKmh: 30,
        source: "simulated",
      });
    });
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it("captura usa intervalo seleccionado y no sincroniza en el ciclo automático", async () => {
    await act(async () => {
      TestRenderer.create(
        React.createElement(Harness, {
          canSync: true,
          interval: 3,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.startTracking();
    });

    expect(mockRunCaptureLoop).toHaveBeenCalledWith(
      expect.any(Function),
      3000,
      expect.any(Function),
    );
    expect(mockEnqueueEvent).toHaveBeenCalled();
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("sincroniza en temporizador independiente de 10s", async () => {
    await act(async () => {
      TestRenderer.create(
        React.createElement(Harness, {
          canSync: true,
          interval: 5,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.startTracking();
    });

    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS);
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue).toHaveBeenCalled();
  });

  it("detener tracking limpia el temporizador y hace sync final", async () => {
    await act(async () => {
      TestRenderer.create(
        React.createElement(Harness, {
          canSync: true,
          interval: 5,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    await act(async () => {
      await latest!.startTracking();
    });
    mockSyncPendingQueue.mockClear();

    await act(async () => {
      await latest!.stopTracking();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);

    mockSyncPendingQueue.mockClear();
    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS * 2);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("Capturar ahora conserva sincronización manual", async () => {
    await act(async () => {
      TestRenderer.create(
        React.createElement(Harness, {
          canSync: true,
          interval: 5,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.captureOnce();
    });
    expect(mockEnqueueEvent).toHaveBeenCalled();
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
  });

  it("desmontar limpia el temporizador de sync", async () => {
    let renderer: TestRenderer.ReactTestRenderer | undefined;
    await act(async () => {
      renderer = TestRenderer.create(
        React.createElement(Harness, {
          canSync: true,
          interval: 5,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    await act(async () => {
      await latest!.startTracking();
    });
    mockSyncPendingQueue.mockClear();

    await act(async () => {
      renderer?.unmount();
    });

    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS * 2);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });
});
