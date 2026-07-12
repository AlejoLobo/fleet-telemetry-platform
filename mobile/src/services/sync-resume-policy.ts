// Política de reanudación de sincronización al cambiar sesión/red.

export function shouldTriggerSyncResume(
  previousReadyToSync: boolean,
  canSync: boolean,
  isOnline: boolean,
): boolean {
  const readyToSync = canSync && isOnline;
  return readyToSync && !previousReadyToSync;
}

export function runSyncResumeEffect(
  previousReadyToSync: boolean,
  canSync: boolean,
  isOnline: boolean,
  syncNow: () => void | Promise<unknown>,
): { nextPreviousReadyToSync: boolean; triggered: boolean } {
  const readyToSync = canSync && isOnline;
  const triggered = readyToSync && !previousReadyToSync;
  if (triggered) {
    void Promise.resolve(syncNow()).catch(() => undefined);
  }
  return { nextPreviousReadyToSync: readyToSync, triggered };
}
