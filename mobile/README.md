# Fleet Telemetry Mobile

App React Native Expo para conductores con cola offline-first y sincronización autenticada.

## Stack

- Expo 52 + React Native
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
| `EXPO_PUBLIC_VEHICLE_ID` | Vehículo por defecto |
| `EXPO_PUBLIC_DRIVER_ID` | Conductor por defecto |

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

Jest (`jest-expo`) cubre auth, telemetry-api, sync-policy, coordinator y operaciones de cola.
