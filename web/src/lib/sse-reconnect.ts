export const BASE_RECONNECT_MS = 1000;
export const MAX_RECONNECT_MS = 30000;

export function computeReconnectDelay(attempt: number): number {
  const exponential = Math.min(MAX_RECONNECT_MS, BASE_RECONNECT_MS * 2 ** attempt);
  const jitter = Math.floor(Math.random() * 500);
  return exponential + jitter;
}
