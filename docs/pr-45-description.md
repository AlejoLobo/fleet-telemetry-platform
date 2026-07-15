## Resumen

Introduce `DeviceId` (UUID inmutable) como identidad técnica de flota, separada de `VehicleName` (`VH-###`).

## Backend

- Registro `fleet_devices` + pipeline por `DeviceId`
- Migración PostgreSQL/Timescale **v7**
- Enrolamiento demo `POST /api/auth/device-token` solo con `AllowDemoDeviceEnrollment=true` en Development; **prohibido en Production** (no es enrolamiento productivo / attestation)

## Mobile

- Cola SQLite **schema v5** con `readyDbPromise` (una apertura + migración por proceso)
- Captura fija **cada 5 segundos**; sin selector de frecuencia
- Sync single-flight con generaciones (`requestedGeneration` / `processedGeneration`)
- Validación nativa Android: procedimiento en `docs/mobile-sqlite-migration.md` (**manual pendiente** en CI cloud)

## Web / SSE

- Selector visual **solo** 5 / 10 / 15 / 20 s (default **5**); legado normalizado en `localStorage`
- Buffer SSE; alertas inmediatas; Actualizar hace flush
- Playwright en CI con `NEXT_PUBLIC_E2E_TEST_MODE` (Demo sembrado + inyector `window.__FLEET_E2E__`; no activo en builds normales)

## Base del PR

`feature/device-identity` → **`develop`**

No se realizó merge.
