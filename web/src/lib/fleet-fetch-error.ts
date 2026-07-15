import { ApiError } from "@/lib/api-client";
import { getApiBaseUrl } from "@/lib/utils";

/** Mensaje de error de carga de flota legible (distingue HTTP vs red/CORS). */
export function resolveFleetFetchError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) {
      return `El backend rechazó la consulta (429). Revisa RateLimiting o vuelve a intentar.`;
    }
    return `Error del backend (${error.status}): ${error.message}`;
  }

  if (error instanceof TypeError || (error instanceof Error && /Failed to fetch|NetworkError|fetch/i.test(error.message))) {
    return `No se pudo conectar con el backend (${getApiBaseUrl()}). Verifica Docker, la API en el puerto 5000 y CORS (usa el mismo host: localhost o 127.0.0.1).`;
  }

  if (error instanceof Error && error.message) {
    return error.message;
  }

  return `No se pudo cargar la flota desde ${getApiBaseUrl()}.`;
}
