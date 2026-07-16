/** Mensaje legible de carga de flota (distingue 429, HTTP y red/CORS). */
export function resolveFleetFetchError(error: unknown): string {
  const status = typeof error === "object" && error !== null && "status" in error
    ? Number((error as { status?: unknown }).status)
    : undefined;
  const retryAfterSeconds = typeof error === "object" && error !== null && "retryAfterSeconds" in error
    ? Number((error as { retryAfterSeconds?: unknown }).retryAfterSeconds)
    : undefined;
  const message = error instanceof Error ? error.message : undefined;

  if (status === 429) {
    return retryAfterSeconds != null && retryAfterSeconds > 0
      ? `El backend limitó temporalmente las solicitudes. Intenta nuevamente después de ${retryAfterSeconds}s.`
      : "El backend limitó temporalmente las solicitudes. Intenta nuevamente después del tiempo indicado.";
  }

  if (typeof status === "number" && Number.isFinite(status) && status >= 400) {
    return `Error del backend (${status}): ${message ?? "respuesta no exitosa"}`;
  }

  if (
    error instanceof TypeError
    || (error instanceof Error && /Failed to fetch|NetworkError|fetch/i.test(error.message))
  ) {
    return "No se pudo conectar con el backend. Verifica Docker, el puerto 5000 y la configuración CORS.";
  }

  if (message) {
    return message;
  }

  return "No se pudo cargar la flota.";
}
