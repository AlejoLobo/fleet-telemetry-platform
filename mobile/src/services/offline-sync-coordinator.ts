import {
  claimNextBatch,
  countPendingEvents,
  markClaimedBatchRetryAtomic,
  markEventPermanentFailure,
  markEventsSynced,
  purgeSyncedOlderThan,
  releaseClaimedEvents,
  toPayload,
} from "@/db/offline-queue";
import { handleUnauthorizedFromApi, markForbiddenFromApi } from "@/services/auth-service";
import { getAuthRuntimeSnapshot } from "@/services/auth-runtime";
import { sendBatchEvents, sendSingleEvent, TelemetryApiError } from "@/services/telemetry-api";
import {
  classifySyncError,
  computeBackoffMs,
  getRetryAfterSeconds,
  type SyncErrorClassification,
} from "@/services/sync-policy";
import type { QueuedTelemetryEvent, SyncResult, SyncStatus } from "@/types/telemetry";

const BATCH_SIZE = 25;
let syncInFlight: Promise<SyncResult> | null = null;

type SyncAccumulator = {
  synced: number;
  failed: number;
  retried: number;
  permanentFailures: number;
  status: SyncStatus;
  retryAt?: string;
};

type StepOutcome = {
  stop: boolean;
  classification?: SyncErrorClassification;
  error?: unknown;
  retryAt?: string;
};

function emptyAccumulator(status: SyncStatus = "completed"): SyncAccumulator {
  return { synced: 0, failed: 0, retried: 0, permanentFailures: 0, status };
}

function toSyncResult(accumulator: SyncAccumulator, remaining: number): SyncResult {
  return {
    synced: accumulator.synced,
    failed: accumulator.failed,
    retried: accumulator.retried,
    permanentFailures: accumulator.permanentFailures,
    remaining,
    status: accumulator.status,
    retryAt: accumulator.retryAt,
  };
}

function canStartRemoteSync(): SyncStatus | null {
  const auth = getAuthRuntimeSnapshot();
  if (auth.mode === "unknown") return "auth_status_error";
  if (auth.mode === "disabled") return null;
  if (!auth.token || auth.tokenExpired) return "auth_required";
  if (auth.expiresAtIso && new Date(auth.expiresAtIso).getTime() <= Date.now()) return "auth_required";
  return null;
}

export async function syncPendingQueue(
  isOnline: boolean,
  deviceId: string,
  batchSize = BATCH_SIZE,
): Promise<SyncResult> {
  if (!isOnline) {
    return {
      synced: 0,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: await countPendingEvents(),
      status: "offline",
    };
  }

  const normalizedDeviceId = deviceId.trim();
  if (!normalizedDeviceId) {
    return {
      synced: 0,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: await countPendingEvents(),
      status: "configuration_error",
    };
  }

  const authBlock = canStartRemoteSync();
  if (authBlock) {
    return {
      synced: 0,
      failed: 0,
      retried: 0,
      permanentFailures: 0,
      remaining: await countPendingEvents(),
      status: authBlock,
    };
  }

  if (syncInFlight) return syncInFlight;
  syncInFlight = runSync(normalizedDeviceId, batchSize).finally(() => {
    syncInFlight = null;
  });
  return syncInFlight;
}

/** Libera el mutex de sincronización; solo para pruebas automatizadas. */
export function resetSyncCoordinatorForTests(): void {
  syncInFlight = null;
}

async function runSync(deviceId: string, batchSize: number): Promise<SyncResult> {
  const accumulator = emptyAccumulator();
  await purgeSyncedOlderThan(7);

  while (true) {
    const batch = await claimNextBatch(batchSize, new Date().toISOString());
    if (!batch.length) break;

    if (batch.length === 1) {
      const outcome = await syncSingleEvent(batch[0], accumulator, deviceId);
      if (outcome.stop) break;
      continue;
    }

    const outcome = await syncBatch(batch, accumulator, deviceId);
    if (outcome.stop) break;
  }

  return toSyncResult(accumulator, await countPendingEvents());
}

function buildStoppedOutcome(
  classification: SyncErrorClassification,
  error: unknown,
  retryAt?: string,
): StepOutcome {
  return { stop: true, classification, error, retryAt };
}

async function resolveUnvisitedSiblings(
  unvisited: QueuedTelemetryEvent[],
  stoppingOutcome: StepOutcome,
  accumulator: SyncAccumulator,
): Promise<StepOutcome> {
  if (!unvisited.length) {
    return stoppingOutcome;
  }

  if (!stoppingOutcome.classification || stoppingOutcome.error === undefined) {
    await releaseClaimedEvents(
      unvisited.map((item) => item.eventId),
      "Error de sincronización",
    );
    accumulator.status = accumulator.status === "completed" ? "failed" : accumulator.status;
    return buildStoppedOutcome(
      { action: "stop_unexpected", category: "protocol", status: 0 },
      stoppingOutcome.error ?? new Error("Error de sincronización"),
      stoppingOutcome.retryAt,
    );
  }

  return handleGlobalBatchFailure(
    unvisited,
    stoppingOutcome.classification,
    stoppingOutcome.error,
    accumulator,
  );
}

async function syncBatch(
  batch: QueuedTelemetryEvent[],
  accumulator: SyncAccumulator,
  deviceId: string,
): Promise<StepOutcome> {
  try {
    await sendBatchEvents(batch.map(toPayload), deviceId);
    await markEventsSynced(batch.map((item) => item.eventId));
    accumulator.synced += batch.length;
    return { stop: false };
  } catch (error) {
    const classification = classifySyncError(error);
    if (classification.action === "isolate_validation") {
      return isolateValidationBatch(batch, accumulator, deviceId);
    }
    if (classification.action === "split_payload") {
      return splitAndSendBatch(batch, accumulator, deviceId);
    }
    return handleGlobalBatchFailure(batch, classification, error, accumulator);
  }
}

async function markIndividualPayloadTooLarge(
  eventId: string,
  resolvedIds: Set<string>,
  accumulator: SyncAccumulator,
): Promise<void> {
  await markEventPermanentFailure(eventId, "Payload demasiado grande");
  resolvedIds.add(eventId);
  accumulator.permanentFailures += 1;
  accumulator.failed += 1;
}

async function splitAndSendBatch(
  batch: QueuedTelemetryEvent[],
  accumulator: SyncAccumulator,
  deviceId: string,
): Promise<StepOutcome> {
  if (batch.length === 1) {
    await markIndividualPayloadTooLarge(batch[0].eventId, new Set(), accumulator);
    return { stop: false };
  }

  const midpoint = Math.ceil(batch.length / 2);
  const firstHalf = batch.slice(0, midpoint);
  const secondHalf = batch.slice(midpoint);

  const firstOutcome = await syncBatch(firstHalf, accumulator, deviceId);
  if (firstOutcome.stop) {
    return resolveUnvisitedSiblings(secondHalf, firstOutcome, accumulator);
  }

  const secondOutcome = await syncBatch(secondHalf, accumulator, deviceId);
  if (secondOutcome.stop) {
    return secondOutcome;
  }

  return { stop: false };
}

async function isolateValidationBatch(
  batch: QueuedTelemetryEvent[],
  accumulator: SyncAccumulator,
  deviceId: string,
): Promise<StepOutcome> {
  const resolvedIds = new Set<string>();

  for (let index = 0; index < batch.length; index += 1) {
    const item = batch[index];
    try {
      await sendSingleEvent(toPayload(item), deviceId);
      await markEventsSynced([item.eventId]);
      resolvedIds.add(item.eventId);
      accumulator.synced += 1;
    } catch (error) {
      const classification = classifySyncError(error);
      if (classification.action === "isolate_validation") {
        await markEventPermanentFailure(item.eventId, sanitizeError(error));
        resolvedIds.add(item.eventId);
        accumulator.permanentFailures += 1;
        accumulator.failed += 1;
        continue;
      }

      if (classification.action === "split_payload") {
        await markIndividualPayloadTooLarge(item.eventId, resolvedIds, accumulator);
        continue;
      }

      const stillProcessing = batch.slice(index).filter((candidate) => !resolvedIds.has(candidate.eventId));
      return handleGlobalBatchFailure(stillProcessing, classification, error, accumulator);
    }
  }

  return { stop: false };
}

async function handleGlobalBatchFailure(
  batch: QueuedTelemetryEvent[],
  classification: SyncErrorClassification,
  error: unknown,
  accumulator: SyncAccumulator,
): Promise<StepOutcome> {
  if (!batch.length) return { stop: true, classification, error };

  const eventIds = batch.map((item) => item.eventId);
  const message = sanitizeError(error);

  if (classification.action === "stop_auth_required") {
    await releaseClaimedEvents(eventIds, message);
    await handleUnauthorizedFromApi();
    accumulator.status = "auth_required";
    return buildStoppedOutcome(classification, error);
  }

  if (classification.action === "stop_forbidden") {
    await releaseClaimedEvents(eventIds, message);
    markForbiddenFromApi(message);
    accumulator.status = "forbidden";
    return buildStoppedOutcome(classification, error);
  }

  if (classification.action === "stop_configuration") {
    await releaseClaimedEvents(eventIds, message);
    accumulator.status = "configuration_error";
    return buildStoppedOutcome(classification, error);
  }

  if (classification.action === "stop_unexpected") {
    await releaseClaimedEvents(eventIds, message);
    accumulator.status = "failed";
    return buildStoppedOutcome(classification, error);
  }

  if (classification.action === "stop_transient") {
    const retryAt = await markTransientBatchRetry(batch, message, getRetryAfterSeconds(error));
    accumulator.retried += batch.length;
    accumulator.status = "deferred";
    accumulator.retryAt = retryAt;
    return buildStoppedOutcome(classification, error, retryAt);
  }

  await releaseClaimedEvents(eventIds, message);
  accumulator.status = "failed";
  return buildStoppedOutcome(classification, error);
}

async function markTransientBatchRetry(
  batch: QueuedTelemetryEvent[],
  message: string,
  retryAfterSeconds?: number,
): Promise<string> {
  const maxRetryCount = Math.max(...batch.map((item) => item.retryCount));
  const delay = computeBackoffMs(maxRetryCount + 1, retryAfterSeconds);
  const retryAt = new Date(Date.now() + delay).toISOString();
  await markClaimedBatchRetryAtomic(batch.map((item) => item.eventId), retryAt, message);
  return retryAt;
}

async function syncSingleEvent(
  item: QueuedTelemetryEvent,
  accumulator: SyncAccumulator,
  deviceId: string,
): Promise<StepOutcome> {
  try {
    await sendSingleEvent(toPayload(item), deviceId);
    await markEventsSynced([item.eventId]);
    accumulator.synced += 1;
    return { stop: false };
  } catch (error) {
    const classification = classifySyncError(error);
    if (classification.action === "isolate_validation") {
      await markEventPermanentFailure(item.eventId, sanitizeError(error));
      accumulator.permanentFailures += 1;
      accumulator.failed += 1;
      return { stop: false };
    }
    if (classification.action === "split_payload") {
      return splitAndSendBatch([item], accumulator, deviceId);
    }
    return handleGlobalBatchFailure([item], classification, error, accumulator);
  }
}

function sanitizeError(error: unknown): string {
  if (error instanceof TelemetryApiError) return error.sanitizedMessage;
  if (error instanceof Error) return error.message.slice(0, 240);
  return "Error de sincronización";
}
