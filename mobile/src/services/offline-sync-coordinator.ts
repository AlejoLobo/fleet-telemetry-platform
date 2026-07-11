import { claimNextBatch, countPendingEvents, markEventPermanentFailure, markEventRetry, markEventsSynced, purgeSyncedOlderThan, toPayload } from "@/db/offline-queue";
import { sendBatchEvents, sendSingleEvent, TelemetryApiError } from "@/services/telemetry-api";
import type { SyncResult } from "@/types/telemetry";

const BATCH_SIZE = 25;
const MAX_RETRIES = 8;
let syncInFlight: Promise<SyncResult> | null = null;

function backoffMs(retryCount: number, retryAfter?: number): number {
  if (retryAfter && retryAfter > 0) return Math.min(retryAfter * 1000, 300_000);
  const base = Math.min(2000 * 2 ** retryCount, 300_000);
  return base + Math.floor(Math.random() * base * 0.25);
}

function permanent(error: unknown): boolean {
  return error instanceof TelemetryApiError && [400, 401, 403, 422].includes(error.status);
}

function transient(error: unknown): boolean {
  return !(error instanceof TelemetryApiError) || error.status === 0 || error.status === 408 || error.status === 429 || error.status >= 500;
}

export async function syncPendingQueue(isOnline: boolean, batchSize = BATCH_SIZE): Promise<SyncResult> {
  if (!isOnline) return { synced: 0, failed: 0, retried: 0, permanentFailures: 0, remaining: await countPendingEvents() };
  if (syncInFlight) return syncInFlight;
  syncInFlight = runSync(batchSize).finally(() => { syncInFlight = null; });
  return syncInFlight;
}

async function runSync(batchSize: number): Promise<SyncResult> {
  let synced = 0, failed = 0, retried = 0, permanentFailures = 0;
  await purgeSyncedOlderThan(7);
  while (true) {
    const batch = await claimNextBatch(batchSize, new Date().toISOString());
    if (!batch.length) break;
    try {
      const payloads = batch.map(toPayload);
      if (payloads.length === 1) await sendSingleEvent(payloads[0]); else await sendBatchEvents(payloads);
      await markEventsSynced(batch.map((b) => b.eventId));
      synced += batch.length;
    } catch (error) {
      for (const item of batch) {
        if (permanent(error)) { await markEventPermanentFailure(item.eventId, String(error)); permanentFailures++; }
        else if (transient(error)) {
          const next = item.retryCount + 1;
          if (next > MAX_RETRIES) { await markEventPermanentFailure(item.eventId, "Max retries exceeded"); permanentFailures++; }
          else {
            const delay = backoffMs(next, error instanceof TelemetryApiError ? error.retryAfterSeconds : undefined);
            await markEventRetry(item.eventId, next, new Date(Date.now() + delay).toISOString(), String(error));
            retried++;
          }
        } else { await markEventPermanentFailure(item.eventId, String(error)); permanentFailures++; }
        failed++;
      }
    }
  }
  return { synced, failed, retried, permanentFailures, remaining: await countPendingEvents() };
}
