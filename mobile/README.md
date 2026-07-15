# Fleet Telemetry Mobile

App React Native Expo para conductores con cola offline-first y sincronización autenticada.

## Stack

- Expo 54 + React Native
- SQLite (`expo-sqlite`) para cola local
- `expo-secure-store` para JWT (nunca en SQLite ni variables públicas)
- NetInfo para detectar conectividad
- `expo-location` con fallback a coordenadas simuladas

## Autenticación

Al iniciar la app:

1. Consulta `GET /api/auth/status`.
2. Si `enabled=false`: sincroniza sin header `Authorization`.
3. Si `enabled=true`: restaura token de SecureStore, valida expiración local y exige login si falta.
4. `POST /api/auth/login` guarda JWT y expiración; habilita sync pendiente.
5. Logout elimina token; **la cola SQLite no se borra**.

### Comportamiento ante errores de auth

| HTTP | Acción |
|------|--------|
| 401 | Elimina token, pausa sync, libera eventos reclamados sin incrementar `retryCount` |
| 403 | Conserva token/cola, pausa sync, estado `forbidden` |

## Matriz batch (`sendBatchEvents`)

| Error | Política |
|-------|----------|
| 2xx | Marca lote `synced` |
| 400/422 | Fallback individual; solo el evento inválido → `permanent_failure` |
| 401/403 | Sin fallback; libera lote; detiene corrida |
| 408/429/5xx/red | Sin fallback; `markBatchRetry` + backoff/`Retry-After` |
| 404/405/415 | `configuration_error`; conserva eventos |
| 413 | Divide lote por mitades; evento único → `permanent_failure` |

`SyncResult.status`: `completed`, `offline`, `auth_required`, `forbidden`, `deferred`, `configuration_error`, `failed`.

## Configuración

```bash
cp .env.example .env
```

| Variable | Descripción |
|----------|-------------|
| `EXPO_PUBLIC_API_URL` | Backend .NET |
| `EXPO_PUBLIC_VEHICLE_ID` | Vehículo fijo de la sesión (solo cambia al reiniciar Expo Go) |
| `EXPO_PUBLIC_DRIVER_ID` | Conductor fijo de la sesión (solo cambia al reiniciar Expo Go) |
| `EXPO_PUBLIC_ALLOW_SIMULATED_LOCATION` | `true` para permitir GPS simulado en desarrollo |

Vehículo y conductor se leen solo desde estos parámetros: la UI los muestra en solo lectura. Para cambiarlos, cierra Expo Go, edita `mobile/.env` y vuelve a abrir con `npx expo start -c`.

No usar `EXPO_PUBLIC_JWT` ni credenciales en variables públicas.

## Comandos

```bash
cd mobile
npm ci
npm run typecheck
npm run test:ci
npm run export
npx expo start
```

## Pruebas

`npm run test:ci` ejecuta Jest (`jest-expo`) con cobertura (`text` + `json-summary`) sobre `src/services`, `src/db` y `src/hooks`. No hay umbral porcentual global obligatorio. El directorio `coverage/` está ignorado por Git.

Áreas cubiertas (archivos representativos):

| Área | Archivo |
|------|---------|
| Auth + SecureStore + cola | `src/__tests__/auth-service.test.ts`, `auth-expiration.test.ts`, `auth-expiration-integration.test.ts` |
| telemetry-api (401/403, captura) | `src/__tests__/telemetry-api.test.ts` |
| Cola SQLite / estados terminales / EventId | `src/__tests__/offline-queue.test.ts` |
| Sync batch / policy | `src/__tests__/offline-sync-coordinator.test.ts`, `sync-policy.test.ts` |
| Fallback parcial / 413 | `src/__tests__/offline-sync-fallback-sqlite.test.ts`, `offline-sync-fallback-413-sqlite.test.ts`, `offline-sync-split-sqlite.test.ts` |
| Reanudación post-login | `src/__tests__/resume-sync.test.ts`, `use-driver-telemetry-resume.test.ts` |
| Ubicación simulada explícita | `src/__tests__/location-provider.test.ts` |
