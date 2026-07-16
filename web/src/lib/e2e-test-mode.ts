/** Modo de pruebas E2E (solo con NEXT_PUBLIC_E2E_TEST_MODE=true en build). */

export function isE2eTestMode(): boolean {
  return process.env.NEXT_PUBLIC_E2E_TEST_MODE === "true";
}

export function getE2eSeed(): number {
  const raw = process.env.NEXT_PUBLIC_E2E_SEED;
  const parsed = raw != null && raw !== "" ? Number(raw) : NaN;
  return Number.isFinite(parsed) ? parsed >>> 0 : 12_345;
}
