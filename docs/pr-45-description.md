## Resumen

Introduce `DeviceId` (UUID inmutable) como identidad técnica de flota, separada de `VehicleName` (nombre visible editable, `VH-###` asignado en backend).

## Backend

- Registro `fleet_devices` + asignación atómica de nombres
- `POST /api/devices/register` y `PATCH /api/devices/{deviceId}/name`
- Pipeline Kafka/telemetría/consultas usan `DeviceId`
- Migración PostgreSQL/Timescale **v7**: columnas `device_id` UUID + backfill documentado
- Seguridad: claims `device_id` / `telemetry:write` / `device:manage`
- **`POST /api/auth/device-token`**: enrolamiento MVP (credenciales + DeviceId → JWT de dispositivo)
- Login operador común: permisos de lectura/ops **sin** `telemetry:write`
- Login administrador (`Auth:AdminUsername` / `Auth:AdminPassword`): incluye `device:manage`, **sin** publicar telemetría

## Mobile

- Identidad estable + cola SQLite (**schema v4**)
- Enrolamiento con token de dispositivo; `canSync` exige `sessionKind=device`, `telemetry:write` y `device_id` coincidente
- Captura fija **cada 5 segundos** (sin selector de frecuencia)
- Sync desacoplada de la captura (single-flight en coordinador offline)
- Fallos de sync reportan `status: failed` (no `completed`)

## Web / SSE

- Dashboard por `deviceId` + `vehicleName`
- Selector de actualización visual: Tiempo real / 5 / 10 / 30 / 60 s
- Buffer SSE + flush por intervalo; alertas y Actualizar inmediatos
- KPI alineados con el snapshot visual (`displayGlobalAnalytics`)
- Restauración segura de preferencia post-hidratación (sin mismatch SSR)

## Pruebas ejecutadas (local)

- Mobile: `typecheck` + `test:ci` (161 tests)
- Web: `lint` + `typecheck` + `test:ci` + `build` + Playwright (4 scenarios)
- Backend Application.Tests (DeviceToken / ingest auth / devices): verde
- Integration.Tests / Docker compose: limitados por entorno cloud (sin Docker daemon)

## Limitaciones conscientes / riesgos

- Enrolamiento MVP por credenciales demo: en producción reemplazar por attestation, mTLS o enrollment firmado
- Admin demo opcional vía configuración explícita
- Flota truncada: totales globales conservan Ops backend; alertas abiertas se recalculan del snapshot visible
- `expo-doctor`: patch mismatch menor de Expo (`54.0.35` vs `~54.0.36`)

## Validación sugerida

```bash
curl -s -X POST "$API/api/auth/device-token" \
  -H 'Content-Type: application/json' \
  -d '{"deviceId":"<uuid>","username":"admin","password":"<demo>"}'

cd mobile && npm ci && npm run typecheck && npm run test:ci
cd web && npm ci && npm run lint && npm run typecheck && npm run test:ci && npm run test:e2e
```

## Base del PR

`feature/device-identity` → `main`.

No se realizó merge.

> Nota: el token de CI de este entorno no puede editar el cuerpo del PR vía GraphQL; aplicar con:
> `gh pr edit 45 --body-file docs/pr-45-description.md`
