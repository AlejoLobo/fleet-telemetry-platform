import React from "react";
import TestRenderer, { act } from "react-test-renderer";

const mockSyncPendingQueue = jest.fn();
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
  enqueueEvent: jest.fn(),
  countPendingEvents: jest.fn(async () => 0),
  resetOfflineQueueForTests: jest.fn(),
}));

jest.mock("@/services/location-provider", () => ({
  getCurrentReading: jest.fn(),
  runCaptureLoop: jest.fn(),
}));

jest.mock("@/utils/id", () => ({
  generateEventId: jest.fn(async () => "generated-event-id"),
}));

import { useDriverTelemetry } from "@/hooks/use-driver-telemetry";

function Harness({ canSync }: { canSync: boolean }) {
  useDriverTelemetry("VH-001", "DRV-001", canSync);
  return null;
}

describe("useDriverTelemetry reanudación integrada", () => {
  beforeEach(() => {
    mockIsOnline = true;
    jest.clearAllMocks();
    mockSyncPendingQueue.mockResolvedValue({
      synced: 0,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: 0,
      status: "completed",
    });
  });

  it("montaje_apta_dispara_syncPendingQueue_una_vez", async () => {
    let renderer: TestRenderer.ReactTestRenderer | undefined;
    await act(async () => {
      renderer = TestRenderer.create(React.createElement(Harness, { canSync: true }));
      await Promise.resolve();
    });

    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true);

    await act(async () => {
      renderer?.update(React.createElement(Harness, { canSync: true }));
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
    renderer?.unmount();
  });

  it("login_offline_y_recuperacion_red_dispara_sync_exactamente_una_vez_mas", async () => {
    mockIsOnline = false;
    let renderer: TestRenderer.ReactTestRenderer | undefined;

    await act(async () => {
      renderer = TestRenderer.create(React.createElement(Harness, { canSync: false }));
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();

    await act(async () => {
      renderer?.update(React.createElement(Harness, { canSync: true }));
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).not.toHaveBeenCalled();

    mockIsOnline = true;
    await act(async () => {
      renderer?.update(React.createElement(Harness, { canSync: true }));
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
    expect(mockSyncPendingQueue).toHaveBeenCalledWith(true);
    renderer?.unmount();
  });

  it("perdida_y_recuperacion_de_red_con_canSync_true_dispara_sync", async () => {
    let renderer: TestRenderer.ReactTestRenderer | undefined;

    await act(async () => {
      renderer = TestRenderer.create(React.createElement(Harness, { canSync: true }));
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);

    mockIsOnline = false;
    await act(async () => {
      renderer?.update(React.createElement(Harness, { canSync: true }));
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);

    mockIsOnline = true;
    await act(async () => {
      renderer?.update(React.createElement(Harness, { canSync: true }));
      await Promise.resolve();
    });
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(2);
    renderer?.unmount();
  });
});
