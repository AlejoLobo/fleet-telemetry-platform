## Resumen

Introduce `DeviceId` (UUID inmutable) como identidad técnica de flota, separada de `VehicleName` (nombre visible editable, `VH-###` asignado en backend).

Esta actualización corrige defectos detectados en revisión y en ejecución real de la app móvil (migración SQLite `device_id`, carrera del coordinador de sync, sesión de otro dispositivo, selector web 5/10/15/20, Playwright en CI y restricción de enrolamiento demo).

## Backend

- Registro `fleet_devices` + asignación atómica de nombres
- `POST /api/devices/register` y `PATCH /api/devices/{deviceId}/name`
- Pipeline Kafka/telemetría/consultas usan `DeviceId`
- Migración PostgreSQL/Timescale **v7**: columnas `device_id` UUID + backfill documentado
- Seguridad: claims `device_id` / `telemetry:write` / `device:manage`
- **`POST /api/auth/device-token`**: enrolamiento MVP demo (credenciales + DeviceId → JWT)
  - Requiere `Auth:AllowDemoDeviceEnrollment=true`
  - **Prohibido en Production** (endpoint 403 + fallo de arranque si el flag está en true)
- Login operador: permisos de lectura/ops **sin** `telemetry:write`
- Login administrador opcional: incluye `device:manage`, **sin** publicar telemetría

## Mobile

- Identidad estable + cola SQLite (**schema v5**)
- **Migración real e idempotente** de `device_id`: `PRAGMA table_info` + `ALTER TABLE … ADD COLUMN device_id TEXT` + backfill; no depende solo de `schema_meta`
- Enrolamiento con token de dispositivo; `canSync` exige `sessionKind=device`, `telemetry:write` y `device_id` coincidente
- Si el JWT es de otro DeviceId: se invalida la sesión, se elimina el token y se muestra el formulario de enrolamiento (la cola SQLite se conserva)
- Captura fija **cada 5 segundos** (sin selector de frecuencia; texto informativo: “Captura automática cada 5 segundos.”)
- Sync desacoplada de la captura; **single-flight** con re-chequeo tras `await` (`ActiveSync` por DeviceId + `rerunRequested`)
- Fallos de sync reportan `status: failed` (no `completed`)

## Web / SSE

- Dashboard por `deviceId` + `vehicleName`
- Selector de actualización visual **solo**: 5 / 10 / 15 / 20 s (default **5**)
- Valores legados (`realtime`, `30`, `60`) migran a **5**
- Buffer SSE + flush por intervalo; alertas, errores, conexión y **Actualizar** son inmediatos
- KPI alineados con el snapshot visual (`displayGlobalAnalytics`)
- Restauración segura de preferencia post-hidratación (sin mismatch SSR)

## CI

- Job **Web E2E Playwright**: `npm ci` → `build` → Chromium → `npm run test:e2e` (webServer `next start` en CI)

## Pruebas

- Mobile: typecheck + test:ci (migración legacy SQLite, timing real 0/5/10/15/20 s, coordinador A/B/C, sesión mismatch)
- Web: lint + typecheck + test:ci + build + Playwright (opciones, persistencia, teclado, migración legado)
- Backend Application.Tests: DeviceToken + ConfigurationValidator (demo enrollment)

## Limitaciones conscientes

- Enrolamiento demo **no** es attestation; solo Development/Demo con flag explícito
- Flota truncada: totales globales conservan Ops backend; alertas abiertas se recalculan del snapshot visible
- `expo-doctor`: posible patch mismatch menor de Expo

## Validación sugerida

```bash
cd mobile && npm ci && npm run typecheck && npm run test:ci
cd web && npm ci && npm run lint && npm run typecheck && npm run test:ci && npm run build && npm run test:e2e
dotnet test backend/FleetTelemetry.Application.Tests --configuration Release
```

## Base del PR

`feature/device-identity` → **`develop`**

No se realizó merge ni fusión de ramas.
