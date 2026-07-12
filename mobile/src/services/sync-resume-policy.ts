// Política de reanudación de sincronización al cambiar sesión/red.

export function shouldTriggerSyncResume(
  previousCanSync: boolean,
  nextCanSync: boolean,
  isOnline: boolean,
): boolean {
  return isOnline && nextCanSync && !previousCanSync;
}

export function runSyncResumeEffect(
  previousCanSync: boolean,
  canSync: boolean,
  isOnline: boolean,
  syncNow: () => void | Promise<unknown>,
): { nextPreviousCanSync: boolean; triggered: boolean } {
  const triggered = shouldTriggerSyncResume(previousCanSync, canSync, isOnline);
  if (triggered) {
    void Promise.resolve(syncNow()).catch(() => undefined);
  }
  return { nextPreviousCanSync: canSync, triggered };
}
