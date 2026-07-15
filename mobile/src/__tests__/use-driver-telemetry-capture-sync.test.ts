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
  syncPendingQueue: (...args: unknown[]) => mockSyncPendingQueue(...args),
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

const DEVICE_ID = "stable-device-xyz-001";
const VEHICLE_ID = "VH-001";

function Harness({
  canSync,
  interval,
  deviceId = DEVICE_ID,
  vehicleId = VEHICLE_ID,
  onReady,
}: {
  canSync: boolean;
  interval: 3 | 5 | 10 | 15;
  deviceId?: string;
  vehicleId?: string;
  onReady: (api: HookApi) => void;
}) {
  const api = useDriverTelemetry(deviceId, vehicleId, "DRV-001", canSync, interval);
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

  async function mount(canSync = true, interval: 3 | 5 | 10 | 15 = 5) {
    await act(async () => {
      TestRenderer.create(
        React.createElement(Harness, {
          canSync,
          interval,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });
  }

  it("captura usa intervalo seleccionado y no sincroniza en el ciclo automático", async () => {
    await mount(true, 3);
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
    await mount();
    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.startTracking();
    });

    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS);
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, DEVICE_ID);
  });

  it("detener tracking limpia el temporizador y hace sync final", async () => {
    await mount();
    await act(async () => {
      await latest!.startTracking();
    });
    mockSyncPendingQueue.mockClear();

    await act(async () => {
      await latest!.stopTracking();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, DEVICE_ID);

    mockSyncPendingQueue.mockClear();
    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS * 2);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("Capturar ahora conserva sincronización manual", async () => {
    await mount();
    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.captureOnce();
    });
    expect(mockEnqueueEvent).toHaveBeenCalled();
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, DEVICE_ID);
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

  it("canSync false durante tracking elimina el timer", async () => {
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
      renderer?.update(
        React.createElement(Harness, {
          canSync: false,
          interval: 5,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS * 2);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("canSync true tras false recrea el timer y sincroniza cada 10s", async () => {
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

    await act(async () => {
      renderer?.update(
        React.createElement(Harness, {
          canSync: false,
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
      renderer?.update(
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

    // resume effect puede disparar sync inmediata
    const afterResume = mockSyncPendingQueue.mock.calls.length;
    expect(afterResume).toBeGreaterThanOrEqual(1);

    mockSyncPendingQueue.mockClear();
    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, DEVICE_ID);

    mockSyncPendingQueue.mockClear();
    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, DEVICE_ID);
  });

  it("nunca crea dos timers simultáneos al restaurar canSync", async () => {
    const setIntervalSpy = jest.spyOn(global, "setInterval");
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
    const afterStart = setIntervalSpy.mock.calls.filter(
      (c) => c[1] === SYNC_INTERVAL_MILLISECONDS,
    ).length;

    await act(async () => {
      renderer?.update(
        React.createElement(Harness, {
          canSync: false,
          interval: 5,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    await act(async () => {
      renderer?.update(
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

    const syncIntervals = setIntervalSpy.mock.calls.filter(
      (c) => c[1] === SYNC_INTERVAL_MILLISECONDS,
    ).length;
    expect(syncIntervals).toBe(afterStart + 1);
    setIntervalSpy.mockRestore();
  });

  it("perder red no elimina eventos en cola", async () => {
    mockCountPendingEvents.mockResolvedValue(3);
    await mount();
    await act(async () => {
      await latest!.startTracking();
    });

    mockIsOnline = false;
    mockSyncPendingQueue.mockResolvedValue({
      synced: 0,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: 3,
      status: "offline",
    });

    await act(async () => {
      await latest!.refreshPendingCount();
    });
    expect(latest!.pendingCount).toBe(3);
  });

  it("un rechazo de syncNow en el timer no deja unhandled rejection y mantiene tracking", async () => {
    const unhandled: unknown[] = [];
    const onUnhandled = (reason: unknown) => {
      unhandled.push(reason);
    };
    process.on("unhandledRejection", onUnhandled);

    mockSyncPendingQueue.mockRejectedValue(new Error("sync temporal falló"));
    await mount();
    await act(async () => {
      await latest!.startTracking();
    });

    await act(async () => {
      jest.advanceTimersByTime(SYNC_INTERVAL_MILLISECONDS);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(latest!.tracking).toBe(true);
    expect(latest!.error).toContain("sync temporal falló");
    expect(unhandled).toHaveLength(0);
    process.off("unhandledRejection", onUnhandled);
  });

  it("usa deviceId separado de vehicleId al sincronizar", async () => {
    await act(async () => {
      TestRenderer.create(
        React.createElement(Harness, {
          canSync: true,
          interval: 5,
          deviceId: "phys-device-99",
          vehicleId: "VH-OTHER",
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
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, "phys-device-99");
    expect(mockEnqueueEvent.mock.calls[0][0].vehicleId).toBe("VH-OTHER");
  });
});
