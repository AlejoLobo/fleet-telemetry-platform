import { createSqliteMemoryDb, getSqliteRows, resetSqliteMemory } from "@/__tests__/helpers/sqlite-memory";

const mockMemoryDb = createSqliteMemoryDb();

jest.mock("expo-sqlite", () => ({
  openDatabaseAsync: jest.fn(async () => mockMemoryDb),
}));

import {
  claimNextBatch,
  enqueueEvent,
  markBatchRetry,
  releaseClaimedEvents,
  releaseEventsToPending,
  resetOfflineQueueForTests,
} from "@/db/offline-queue";

const basePayload = {
  eventId: "11111111-1111-1111-1111-111111111111",
  vehicleId: "VH-001",
  driverId: "DRV-001",
  timestamp: "2026-07-10T10:00:00Z",
  latitude: 4.65,
  longitude: -74.08,
  speedKmh: 40,
  fuelLevelPercent: 70,
  batteryPercent: 90,
};

describe("offline-queue operations", () => {
  beforeEach(() => {
    resetSqliteMemory();
    resetOfflineQueueForTests();
    jest.clearAllMocks();
  });

  it("releaseEventsToPending_limpia_locked_at", async () => {
    await enqueueEvent(basePayload);
    const now = new Date().toISOString();
    const claimed = await claimNextBatch(10, now);
    expect(claimed[0].lockedAt).not.toBeNull();

    await releaseEventsToPending([basePayload.eventId], "auth");
    const row = getSqliteRows().find((item) => item.event_id === basePayload.eventId);
    expect(row?.locked_at).toBeNull();
    expect(row?.status).toBe("pending");
    expect(row?.retry_count).toBe(0);
  });

  it("releaseClaimedEvents_libera_uno_o_varios_eventos", async () => {
    await enqueueEvent(basePayload);
    await enqueueEvent({ ...basePayload, eventId: "22222222-2222-2222-2222-222222222222" });
    const now = new Date().toISOString();
    await claimNextBatch(10, now);

    await releaseClaimedEvents([
      basePayload.eventId,
      "22222222-2222-2222-2222-222222222222",
    ], "forbidden");

    getSqliteRows().forEach((row) => {
      expect(row.locked_at).toBeNull();
      expect(row.status).toBe("pending");
    });
  });

  it("markBatchRetry_incrementa_retryCount_en_errores_transitorios", async () => {
    await enqueueEvent(basePayload);
    const now = new Date().toISOString();
    await claimNextBatch(10, now);

    await markBatchRetry([basePayload.eventId], new Date(Date.now() + 5000).toISOString(), "500", true);
    const row = getSqliteRows().find((item) => item.event_id === basePayload.eventId);
    expect(row?.retry_count).toBe(1);
    expect(row?.locked_at).toBeNull();
    expect(row?.status).toBe("retry");
  });

  it("markBatchRetry_sin_incremento_conserva_retryCount_en_errores_auth", async () => {
    await enqueueEvent(basePayload);
    const now = new Date().toISOString();
    const claimed = await claimNextBatch(10, now);
    const initialRetry = claimed[0].retryCount;

    await markBatchRetry([basePayload.eventId], new Date(Date.now() + 5000).toISOString(), "auth", false);
    const row = getSqliteRows().find((item) => item.event_id === basePayload.eventId);
    expect(row?.retry_count).toBe(initialRetry);
    expect(row?.status).toBe("pending");
    expect(row?.locked_at).toBeNull();
  });

  it("ningun_update_afecta_eventos_ajenos_al_lote", async () => {
    await enqueueEvent(basePayload);
    await enqueueEvent({ ...basePayload, eventId: "22222222-2222-2222-2222-222222222222" });
    const now = new Date().toISOString();
    await claimNextBatch(10, now);

    await releaseEventsToPending([basePayload.eventId], "auth");
    const untouched = getSqliteRows().find((item) => item.event_id === "22222222-2222-2222-2222-222222222222");
    expect(untouched?.status).toBe("processing");
    expect(untouched?.locked_at).not.toBeNull();
  });

  it("operacion_transaccional_ante_fallo_no_deja_estado_inconsistente", async () => {
    await enqueueEvent(basePayload);
    const now = new Date().toISOString();
    await claimNextBatch(10, now);

    mockMemoryDb.withTransactionAsync.mockImplementationOnce(async () => {
      throw new Error("sqlite transaction failed");
    });

    await expect(releaseEventsToPending([basePayload.eventId], "error")).rejects.toThrow("sqlite transaction failed");
    const row = getSqliteRows().find((item) => item.event_id === basePayload.eventId);
    expect(row?.status).toBe("processing");
    expect(row?.locked_at).not.toBeNull();
  });
});
