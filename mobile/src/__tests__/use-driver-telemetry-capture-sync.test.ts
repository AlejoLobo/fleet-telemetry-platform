import React from "react";
import TestRenderer, { act } from "react-test-renderer";
import * as fs from "fs";
import * as path from "path";

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

import {
  TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS,
  TELEMETRY_SYNC_INTERVAL_MILLISECONDS,
  useDriverTelemetry,
} from "@/hooks/use-driver-telemetry";

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

function Harness({
  canSync,
  deviceId = DEVICE_ID,
  onReady,
}: {
  canSync: boolean;
  deviceId?: string;
  onReady: (api: HookApi) => void;
}) {
  const api = useDriverTelemetry(deviceId, "DRV-001", canSync);
  React.useEffect(() => {
    onReady(api);
  }, [api, onReady]);
  return null;
}

describe("useDriverTelemetry captura fija 5s y single-flight", () => {
  let latest: HookApi | null = null;
  let renderer: TestRenderer.ReactTestRenderer | null = null;

  beforeEach(() => {
    jest.useFakeTimers();
    mockIsOnline = true;
    latest = null;
    renderer = null;
    jest.clearAllMocks();
    mockSyncPendingQueue.mockResolvedValue({ ...SYNC_OK });
    mockEnqueueEvent.mockResolvedValue(undefined);
    mockCountPendingEvents.mockResolvedValue(0);
    mockGetCurrentReading.mockResolvedValue({
      latitude: 4.65,
      longitude: -74.08,
      speedKmh: 30,
      source: "simulated",
    });
    mockRunCaptureLoop.mockImplementation(async (onReading, _ms, shouldContinue) => {
      if (shouldContinue()) {
        await onReading({
          latitude: 4.65,
          longitude: -74.08,
          speedKmh: 30,
          source: "simulated",
        });
      }
    });
  });

  afterEach(async () => {
    await act(async () => {
      renderer?.unmount();
    });
    renderer = null;
    latest = null;
    jest.useRealTimers();
  });

  async function mount(canSync = true, deviceId = DEVICE_ID) {
    await act(async () => {
      renderer = TestRenderer.create(
        React.createElement(Harness, {
          canSync,
          deviceId,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });
    expect(latest).not.toBeNull();
  }

  it("captura usa siempre 5000 ms", async () => {
    expect(TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS).toBe(5_000);
    await mount();
    await act(async () => {
      await latest!.startTracking();
    });
    expect(mockRunCaptureLoop).toHaveBeenCalledWith(
      expect.any(Function),
      5_000,
      expect.any(Function),
    );
  });

  it("DriverDashboard no incluye selector de frecuencia", () => {
    const source = fs.readFileSync(
      path.join(__dirname, "../components/DriverDashboard.tsx"),
      "utf8",
    );
    expect(source).not.toMatch(/Frecuencia de registro/);
    expect(source).not.toMatch(/Cada 3 segundos/);
    expect(source).not.toMatch(/captureIntervalSeconds/);
    expect(source).not.toMatch(/handleCaptureIntervalChange/);
    expect(source).not.toMatch(/capture-interval-store/);
    expect(source).toMatch(/Captura fija cada 5 segundos/);
  });

  it("después de capturar guarda primero en SQLite y luego sync", async () => {
    const order: string[] = [];
    mockEnqueueEvent.mockImplementation(async () => {
      order.push("enqueue");
    });
    mockSyncPendingQueue.mockImplementation(async () => {
      order.push("sync");
      return { ...SYNC_OK };
    });

    await mount();
    mockSyncPendingQueue.mockClear();
    order.length = 0;

    await act(async () => {
      await latest!.captureOnce();
    });

    expect(order[0]).toBe("enqueue");
    expect(order.indexOf("enqueue")).toBeLessThan(order.indexOf("sync"));
  });

  it("online y autorizado solicita sync después de capturar", async () => {
    await mount(true);
    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.captureOnce();
    });
    expect(mockEnqueueEvent).toHaveBeenCalled();
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, DEVICE_ID);
  });

  it("offline no intenta enviar pero conserva la captura", async () => {
    mockIsOnline = false;
    await mount(true);
    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.captureOnce();
    });
    expect(mockEnqueueEvent).toHaveBeenCalled();
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("dos capturas rápidas no crean dos sincronizaciones simultáneas", async () => {
    let inFlight = 0;
    let maxInFlight = 0;
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });

    mockSyncPendingQueue.mockImplementation(async () => {
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await gate;
      inFlight -= 1;
      return { ...SYNC_OK };
    });

    await mount();
    // Liberar sync de montaje (resume)
    await act(async () => {
      release();
      await Promise.resolve();
      await Promise.resolve();
    });

    let release2!: () => void;
    const gate2 = new Promise<void>((resolve) => {
      release2 = resolve;
    });
    mockSyncPendingQueue.mockImplementation(async () => {
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await gate2;
      inFlight -= 1;
      return { ...SYNC_OK };
    });
    mockSyncPendingQueue.mockClear();
    maxInFlight = 0;
    inFlight = 0;

    await act(async () => {
      void latest!.captureOnce();
      void latest!.captureOnce();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
    expect(maxInFlight).toBe(1);

    await act(async () => {
      release2();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue.mock.calls.length).toBeGreaterThanOrEqual(2);
    expect(maxInFlight).toBe(1);
  });

  it("una captura durante sync activa genera ejecución posterior", async () => {
    let releaseSlow!: () => void;
    const slowGate = new Promise<void>((resolve) => {
      releaseSlow = resolve;
    });
    let calls = 0;

    // Montaje: sync inmediata (resume) sin bloquear
    mockSyncPendingQueue.mockResolvedValue({ ...SYNC_OK });
    await mount();

    calls = 0;
    mockSyncPendingQueue.mockImplementation(async () => {
      calls += 1;
      if (calls === 1) {
        await slowGate;
      }
      return { ...SYNC_OK };
    });
    mockSyncPendingQueue.mockClear();

    await act(async () => {
      void latest!.syncNow();
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);

    // No await captureOnce: compartiría el promise bloqueado
    await act(async () => {
      void latest!.captureOnce();
      await Promise.resolve();
    });

    await act(async () => {
      releaseSlow();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  it("temporizador de respaldo usa 5000 ms", async () => {
    expect(TELEMETRY_SYNC_INTERVAL_MILLISECONDS).toBe(5_000);
    await mount();
    await act(async () => {
      await latest!.startTracking();
    });
    mockSyncPendingQueue.mockClear();

    await act(async () => {
      jest.advanceTimersByTime(TELEMETRY_SYNC_INTERVAL_MILLISECONDS);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true, DEVICE_ID);
  });

  it("detener tracking limpia el timer", async () => {
    await mount();
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
      jest.advanceTimersByTime(TELEMETRY_SYNC_INTERVAL_MILLISECONDS * 2);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("desmontar el hook limpia el timer", async () => {
    await mount();
    await act(async () => {
      await latest!.startTracking();
    });
    mockSyncPendingQueue.mockClear();

    await act(async () => {
      renderer?.unmount();
      renderer = null;
    });

    await act(async () => {
      jest.advanceTimersByTime(TELEMETRY_SYNC_INTERVAL_MILLISECONDS * 2);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("recuperar conexión sincroniza la cola", async () => {
    mockIsOnline = false;
    await mount(true);
    mockSyncPendingQueue.mockClear();

    mockIsOnline = true;
    await act(async () => {
      renderer?.update(
        React.createElement(Harness, {
          canSync: true,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue).toHaveBeenCalled();
  });

  it("los eventos siguen pasando por SQLite", async () => {
    await mount();
    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.captureOnce();
    });
    expect(mockEnqueueEvent).toHaveBeenCalledWith(
      expect.objectContaining({
        eventId: "generated-event-id",
        deviceId: DEVICE_ID,
      }),
      "simulated",
    );
  });

  it("errores de sync no detienen permanentemente el tracking", async () => {
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
    mockSyncPendingQueue.mockClear();
    mockSyncPendingQueue.mockRejectedValue(new Error("sync temporal falló"));

    await act(async () => {
      jest.advanceTimersByTime(TELEMETRY_SYNC_INTERVAL_MILLISECONDS);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(latest!.tracking).toBe(true);
    expect(latest!.error).toContain("sync temporal falló");
    expect(unhandled).toHaveLength(0);
    process.off("unhandledRejection", onUnhandled);
  });

  it("Capturar ahora conserva sincronización cuando hay red", async () => {
    await mount();
    mockSyncPendingQueue.mockClear();
    await act(async () => {
      await latest!.captureOnce();
    });
    expect(mockEnqueueEvent).toHaveBeenCalled();
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
  });

  it("canSync false durante tracking elimina el timer", async () => {
    await mount(true);
    await act(async () => {
      await latest!.startTracking();
    });
    mockSyncPendingQueue.mockClear();

    await act(async () => {
      renderer?.update(
        React.createElement(Harness, {
          canSync: false,
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    await act(async () => {
      jest.advanceTimersByTime(TELEMETRY_SYNC_INTERVAL_MILLISECONDS * 2);
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();
  });

  it("nunca crea dos timers simultáneos al restaurar canSync", async () => {
    const setIntervalSpy = jest.spyOn(global, "setInterval");
    await mount(true);

    await act(async () => {
      await latest!.startTracking();
    });
    const afterStart = setIntervalSpy.mock.calls.filter(
      (c) => c[1] === TELEMETRY_SYNC_INTERVAL_MILLISECONDS,
    ).length;

    await act(async () => {
      renderer?.update(
        React.createElement(Harness, {
          canSync: false,
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
          onReady: (api) => {
            latest = api;
          },
        }),
      );
      await Promise.resolve();
    });

    const syncIntervals = setIntervalSpy.mock.calls.filter(
      (c) => c[1] === TELEMETRY_SYNC_INTERVAL_MILLISECONDS,
    ).length;
    expect(syncIntervals).toBe(afterStart + 1);
    setIntervalSpy.mockRestore();
  });
});
