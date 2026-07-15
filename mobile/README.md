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

## Identidad de dispositivo

1. `DeviceId` UUID estable en SecureStore (`fleet.device.id`).
2. Antes del sync, `POST /api/devices/register` (idempotente) obtiene `vehicleName` del backend.
3. Los eventos de telemetría llevan `deviceId` (no `vehicleId` / `VH-###`).
4. Header `X-Device-Id` debe coincidir con el payload.
5. Renombrar en UI actualiza solo el nombre visible.

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
| `EXPO_PUBLIC_DRIVER_ID` | Conductor por defecto |

La identidad del vehículo es el `DeviceId` UUID generado en el dispositivo (SecureStore). Mobile **no** genera nombres `VH-###`; el backend los asigna en `POST /api/devices/register`. El nombre visible se puede editar con `PATCH /api/devices/{deviceId}/name` sin cambiar la identidad ni la partición Kafka.

No usar `EXPO_PUBLIC_JWT` ni credenciales en variables públicas.

## Captura de telemetría

Intervalo fijo de **5 segundos** (`TELEMETRY_CAPTURE_INTERVAL_MILLISECONDS`). No hay selector ni configuración de frecuencia en la UI.

Texto informativo: “Captura automática cada 5 segundos.”

## Cola SQLite

La apertura y migración del esquema se ejecutan **una sola vez** por proceso. Detalle de migración `device_id` y validación en dispositivo: [../docs/mobile-sqlite-migration.md](../docs/mobile-sqlite-migration.md).

Limpieza de cache en desarrollo:

```bash
cd mobile
npx expo start -c
npx expo export --clear
```

Si se usa APK o development build antigua, reconstruir e reinstalar/actualizar la app.

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
