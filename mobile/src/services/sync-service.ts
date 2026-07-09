import {
  countPendingEvents,
  getPendingEvents,
  markEventsFailed,
  markEventsSynced,
  resetFailedToPending,
  toPayload,
} from "@/db/offline-queue";
import { sendBatchEvents, sendSingleEvent, TelemetryApiError } from "@/services/telemetry-api";
import type { SyncResult, TelemetryEventPayload } from "@/types/telemetry";

const BATCH_SIZE = 25;

export async function syncPendingQueue(isOnline: boolean): Promise<SyncResult> {
  if (!isOnline) {
    const remaining = await countPendingEvents();
    return { synced: 0, failed: 0, remaining };
  }

  await resetFailedToPending();

  let synced = 0;
  let failed = 0;

  while (true) {
    const pending = await getPendingEvents(BATCH_SIZE);
    if (pending.length === 0) break;

    const payloads = pending.map(toPayload);
    const eventIds = pending.map((e) => e.eventId);

    try {
      if (payloads.length === 1) {
        await sendSingleEvent(payloads[0]);
      } else {
        await sendBatchEvents(payloads);
      }
      await markEventsSynced(eventIds);
      synced += eventIds.length;
    } catch (error) {
      if (error instanceof TelemetryApiError && payloads.length > 1) {
        const partial = await syncOneByOne(payloads);
        synced += partial.synced;
        failed += partial.failed;
      } else {
        await markEventsFailed(eventIds);
        failed += eventIds.length;
      }
      break;
    }
  }

  const remaining = await countPendingEvents();
  return { synced, failed, remaining };
}

async function syncOneByOne(events: TelemetryEventPayload[]): Promise<{ synced: number; failed: number }> {
  let synced = 0;
  let failed = 0;

  for (const event of events) {
    try {
      await sendSingleEvent(event);
      await markEventsSynced([event.eventId]);
      synced += 1;
    } catch {
      await markEventsFailed([event.eventId]);
      failed += 1;
    }
  }

  return { synced, failed };
}
