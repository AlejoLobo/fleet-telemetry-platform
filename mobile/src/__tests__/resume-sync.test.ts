import { createSqliteMemoryDb, getSqliteRows, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();
const mockSyncPendingQueue = jest.fn();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

jest.mock("@/services/offline-sync-coordinator", () => ({
  syncPendingQueue: (...args: unknown[]) => mockSyncPendingQueue(...args),
  resetSyncCoordinatorForTests: jest.fn(),
}));

import { enqueueEvent, getQueueEventByEventId, resetOfflineQueueForTests } from "@/db/offline-queue";
import { runSyncResumeEffect } from "@/services/sync-resume-policy";

const event = {
  eventId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  vehicleId: "VH-001",
  driverId: "DRV-001",
  timestamp: "2026-07-10T10:00:00Z",
  latitude: 4.65,
  longitude: -74.08,
  speedKmh: 40,
  fuelLevelPercent: 70,
  batteryPercent: 90,
};

describe("reanudación post-login", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
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

  it("auth_required_a_authenticated_y_online_una_sola_syncPendingQueue", async () => {
    let syncCalls = 0;
    const syncNow = () => {
      syncCalls += 1;
      return mockSyncPendingQueue(true);
    };

    const result = runSyncResumeEffect(false, true, true, syncNow);
    expect(result.triggered).toBe(true);
    expect(syncCalls).toBe(1);
    expect(mockSyncPendingQueue).toHaveBeenCalledTimes(1);
  });

  it("authenticated_a_authenticated_no_inicia_otra_sincronizacion", () => {
    let syncCalls = 0;
    const result = runSyncResumeEffect(true, true, true, () => {
      syncCalls += 1;
    });
    expect(result.triggered).toBe(false);
    expect(syncCalls).toBe(0);
  });

  it("offline_a_authenticated_no_sincroniza_hasta_recuperar_red", () => {
    let syncCalls = 0;
    const result = runSyncResumeEffect(false, true, false, () => {
      syncCalls += 1;
    });
    expect(result.triggered).toBe(false);
    expect(syncCalls).toBe(0);
  });

  it("auth_disabled_inicia_sincronizacion_anonima_una_sola_vez_con_red", () => {
    let syncCalls = 0;
    const first = runSyncResumeEffect(false, true, true, () => {
      syncCalls += 1;
    });
    const second = runSyncResumeEffect(first.nextPreviousCanSync, true, true, () => {
      syncCalls += 1;
    });
    expect(first.triggered).toBe(true);
    expect(second.triggered).toBe(false);
    expect(syncCalls).toBe(1);
  });

  it("dos_renders_consecutivos_no_crean_dos_disparadores", () => {
    let syncCalls = 0;
    let previous = false;
    for (let i = 0; i < 2; i += 1) {
      const result = runSyncResumeEffect(previous, true, true, () => {
        syncCalls += 1;
      });
      previous = result.nextPreviousCanSync;
    }
    expect(syncCalls).toBe(1);
  });

  it("EventId_permanece_intacto_despues_de_sincronizacion_post_login", async () => {
    await enqueueEvent(event);
    mockSyncPendingQueue.mockResolvedValueOnce({
      synced: 1,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: 0,
      status: "completed",
    });

    runSyncResumeEffect(false, true, true, () => mockSyncPendingQueue(true));
    await Promise.resolve();

    const rows = getSqliteRows().filter((row) => row.event_id === event.eventId);
    expect(rows).toHaveLength(1);
    expect((await getQueueEventByEventId(event.eventId))?.eventId).toBe(event.eventId);
  });
});
