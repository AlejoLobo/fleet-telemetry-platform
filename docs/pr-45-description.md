## Resumen

Introduce `DeviceId` (UUID inmutable) como identidad técnica de flota, separada de `VehicleName` (nombre visible editable, `VH-###` asignado en backend).

Incluye correcciones de revisión: migración SQLite v5 serializada (`readyDbPromise`), captura móvil fija 5 s sin selector, selector web 5/10/15/20 con persistencia normalizada, sync single-flight sin perder reentradas, Playwright en CI y enrolamiento demo restringido.

## Backend

- Registro `fleet_devices` + pipeline Kafka/telemetría por `DeviceId`
- Migración PostgreSQL/Timescale **v7** (`device_id` UUID + backfill)
- `POST /api/auth/device-token`: enrolamiento **MVP demo**
  - Development/Demo: requiere `Auth:AllowDemoDeviceEnrollment=true`
  - Production: **prohibido** (403 + fallo de arranque si el flag está en true)
  - No es attestation; en producción hace falta enrolamiento firmado / mTLS / secreto individual

## Mobile

- Cola SQLite **schema v5**; apertura + migración **una sola vez** por proceso
- Migración real: `PRAGMA table_info` + `ALTER TABLE … ADD COLUMN device_id TEXT`
- Captura fija **cada 5 segundos**; sin selector de frecuencia
- Sync single-flight con generaciones (`requestedGeneration` / `processedGeneration`)
- `validateSessionForLocalDevice` invalida JWT de otro DeviceId (conserva cola)

## Web / SSE

- Selector visual **solo** 5 / 10 / 15 / 20 s (default **5**)
- Valores legados se normalizan y **se reescriben** en `localStorage` como `"5"`
- Buffer SSE + flush; alertas y **Actualizar** inmediatos

## CI

- Job **Web E2E Playwright** (build + Chromium + `next start`)

## Limitaciones

- Enrolamiento demo no es seguridad productiva
- Pruebas SQLite legacy en Jest son simulación de `PRAGMA`; validación nativa Android documentada en `docs/mobile-sqlite-migration.md`

## Base del PR

`feature/device-identity` → **`develop`**

No se realizó merge ni fusión de ramas.
