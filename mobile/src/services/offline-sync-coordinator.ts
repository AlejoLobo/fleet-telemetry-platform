import {
  claimNextBatch,
  countPendingEvents,
  markEventPermanentFailure,
  markEventRetry,
  markEventsSynced,
  purgeSyncedOlderThan,
  toPayload,
} from "@/db/offline-queue";
import { sendBatchEvents, sendSingleEvent, TelemetryApiError } from "@/services/telemetry-api";
import { computeBackoffMs, getRetryAfterSeconds, isPermanentSyncError, isTransientSyncError } from "@/services/sync-policy";
import type { QueuedTelemetryEvent, SyncResult } from "@/types/telemetry";

const BATCH_SIZE = 25;
const MAX_RETRIES = 8;
let syncInFlight: Promise<SyncResult> | null = null;

export async function syncPendingQueue(isOnline: boolean, batchSize = BATCH_SIZE): Promise<SyncResult> {
  if (!isOnline) {
    return {
      synced: 0,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: await countPendingEvents(),
    };
  }
  if (syncInFlight) return syncInFlight;
  syncInFlight = runSync(batchSize).finally(() => {
    syncInFlight = null;
  });
  return syncInFlight;
}

/** Libera el mutex de sincronización; solo para pruebas automatizadas. */
export function resetSyncCoordinatorForTests(): void {
  syncInFlight = null;
}

async function runSync(batchSize: number): Promise<SyncResult> {
  let synced = 0;
  let failed = 0;
  let retried = 0;
  let permanentFailures = 0;

  await purgeSyncedOlderThan(7);

  while (true) {
    const batch = await claimNextBatch(batchSize, new Date().toISOString());
    if (!batch.length) break;

    if (batch.length === 1) {
      const outcome = await syncSingleEvent(batch[0]);
      synced += outcome.synced;
      failed += outcome.failed;
      retried += outcome.retried;
      permanentFailures += outcome.permanentFailures;
      continue;
    }

    try {
      await sendBatchEvents(batch.map(toPayload));
      await markEventsSynced(batch.map((item) => item.eventId));
      synced += batch.length;
    } catch {
      // Fallback evento a evento para no bloquear el lote por un registro inválido.
      for (const item of batch) {
        const outcome = await syncSingleEvent(item);
        synced += outcome.synced;
        failed += outcome.failed;
        retried += outcome.retried;
        permanentFailures += outcome.permanentFailures;
      }
    }
  }

  return {
    synced,
    failed,
    retried,
    permanentFailures,
    remaining: await countPendingEvents(),
  };
}

async function syncSingleEvent(item: QueuedTelemetryEvent): Promise<SyncResult> {
  try {
    await sendSingleEvent(toPayload(item));
    await markEventsSynced([item.eventId]);
    return { synced: 1, failed: 0, retried: 0, permanentFailures: 0, remaining: 0 };
  } catch (error) {
    if (isPermanentSyncError(error)) {
      await markEventPermanentFailure(item.eventId, String(error));
      return { synced: 0, failed: 1, retried: 0, permanentFailures: 1, remaining: 0 };
    }

    if (isTransientSyncError(error)) {
      const next = item.retryCount + 1;
      if (next > MAX_RETRIES) {
        await markEventPermanentFailure(item.eventId, "Max retries exceeded");
        return { synced: 0, failed: 1, retried: 0, permanentFailures: 1, remaining: 0 };
      }

      const delay = computeBackoffMs(next, getRetryAfterSeconds(error));
      await markEventRetry(
        item.eventId,
        next,
        new Date(Date.now() + delay).toISOString(),
        String(error),
      );
      return { synced: 0, failed: 1, retried: 1, permanentFailures: 0, remaining: 0 };
    }

    await markEventPermanentFailure(item.eventId, String(error));
    return { synced: 0, failed: 1, retried: 0, permanentFailures: 1, remaining: 0 };
  }
}
