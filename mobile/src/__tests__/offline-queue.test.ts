import { createSqliteMemoryDb, getSqliteRows, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

import {
  claimNextBatch,
  enqueueEvent,
  getQueueEventByEventId,
  markBatchRetry,
  markClaimedBatchRetryAtomic,
  markEventPermanentFailure,
  markEventsSynced,
  releaseClaimedEvents,
  releaseEventsToPending,
  resetOfflineQueueForTests,
} from "@/db/offline-queue";
import { setFailNextBatchRetry } from "@/__tests__/helpers/sqlite-memory";

const basePayload = {
  eventId: "11111111-1111-1111-1111-111111111111",
  deviceId: "aaaaaaaa-bbbb-4ccc-8ddd-000000000001",
  driverId: "DRV-001",
  timestamp: "2026-07-10T10:00:00Z",
  latitude: 4.65,
  longitude: -74.08,
  speedKmh: 40,
  fuelLevelPercent: 70,
  batteryPercent: 90,
};

async function seedProcessing(eventId: string, status: "processing" | "synced" | "permanent_failure" = "processing") {
  await enqueueEvent({ ...basePayload, eventId });
  const now = new Date().toISOString();
  if (status === "processing") {
    await claimNextBatch(10, now);
    return;
  }
  await claimNextBatch(10, now);
  if (status === "synced") {
    await markEventsSynced([eventId]);
    return;
  }
  await markEventPermanentFailure(eventId, "invalid");
}

describe("offline-queue operations", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    jest.clearAllMocks();
  });

  it("releaseEventsToPending_limpia_locked_at", async () => {
    await seedProcessing(basePayload.eventId);
    await releaseEventsToPending([basePayload.eventId], "auth");
    const row = await getQueueEventByEventId(basePayload.eventId);
    expect(row?.lockedAt).toBeNull();
    expect(row?.status).toBe("pending");
    expect(row?.retryCount).toBe(0);
  });

  it("releaseClaimedEvents_no_cambia_un_permanent_failure", async () => {
    const eventId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    await seedProcessing(eventId, "permanent_failure");
    const affected = await releaseClaimedEvents([eventId], "auth");
    const row = await getQueueEventByEventId(eventId);
    expect(affected).toBe(0);
    expect(row?.status).toBe("permanent_failure");
  });

  it("releaseClaimedEvents_no_cambia_un_synced", async () => {
    const eventId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    await seedProcessing(eventId, "synced");
    const affected = await releaseClaimedEvents([eventId], "auth");
    const row = await getQueueEventByEventId(eventId);
    expect(affected).toBe(0);
    expect(row?.status).toBe("synced");
  });

  it("markBatchRetry_no_cambia_un_permanent_failure", async () => {
    const eventId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    await seedProcessing(eventId, "permanent_failure");
    const affected = await markBatchRetry([eventId], new Date().toISOString(), "500", true);
    const row = await getQueueEventByEventId(eventId);
    expect(affected).toBe(0);
    expect(row?.status).toBe("permanent_failure");
  });

  it("markBatchRetry_no_cambia_un_synced", async () => {
    const eventId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    await seedProcessing(eventId, "synced");
    const affected = await markBatchRetry([eventId], new Date().toISOString(), "500", true);
    const row = await getQueueEventByEventId(eventId);
    expect(affected).toBe(0);
    expect(row?.status).toBe("synced");
  });

  it("solo_los_processing_indicados_son_modificados", async () => {
    const processingId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
    const syncedId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
    await seedProcessing(processingId, "processing");
    await seedProcessing(syncedId, "synced");
    const affected = await releaseClaimedEvents([processingId, syncedId], "forbidden");
    expect(affected).toBe(1);
    expect((await getQueueEventByEventId(processingId))?.status).toBe("pending");
    expect((await getQueueEventByEventId(syncedId))?.status).toBe("synced");
  });

  it("markBatchRetry_incrementa_retryCount_en_errores_transitorios", async () => {
    await seedProcessing(basePayload.eventId);
    await markBatchRetry([basePayload.eventId], new Date(Date.now() + 5000).toISOString(), "500", true);
    const row = await getQueueEventByEventId(basePayload.eventId);
    expect(row?.retryCount).toBe(1);
    expect(row?.lockedAt).toBeNull();
    expect(row?.status).toBe("retry");
  });

  it("markClaimedBatchRetryAtomic_con_retryCount_mayor_que_8_no_permanent_failure", async () => {
    await enqueueEvent(basePayload);
    const now = new Date().toISOString();
    const claimed = await claimNextBatch(10, now);
    const rowBefore = getSqliteRows().find((r) => r.event_id === basePayload.eventId);
    if (rowBefore) rowBefore.retry_count = 9;

    await markClaimedBatchRetryAtomic([basePayload.eventId], new Date(Date.now() + 5000).toISOString(), "HTTP 500");
    const row = await getQueueEventByEventId(basePayload.eventId);
    expect(row?.status).toBe("retry");
    expect(row?.retryCount).toBe(10);
    expect(row?.lockedAt).toBeNull();
  });

  it("markClaimedBatchRetryAtomic_fallo_transaccional_sin_resultado_parcial", async () => {
    const id1 = "11111111-1111-1111-1111-111111111111";
    const id2 = "22222222-2222-2222-2222-222222222222";
    await enqueueEvent({ ...basePayload, eventId: id1 });
    await enqueueEvent({ ...basePayload, eventId: id2 });
    await claimNextBatch(10, new Date().toISOString());

    setFailNextBatchRetry(true);
    mockMemoryDb.withTransactionAsync.mockImplementationOnce(async (callback: () => Promise<void>) => {
      try {
        await callback();
      } catch (error) {
        throw error;
      }
    });

    await expect(
      markClaimedBatchRetryAtomic([id1, id2], new Date().toISOString(), "HTTP 500"),
    ).rejects.toThrow("simulated batch retry failure");

    expect((await getQueueEventByEventId(id1))?.status).toBe("processing");
    expect((await getQueueEventByEventId(id2))?.status).toBe("processing");
    expect((await getQueueEventByEventId(id1))?.retryCount).toBe(0);
    expect((await getQueueEventByEventId(id2))?.retryCount).toBe(0);
  });
});
