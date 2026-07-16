## Resumen

Introduce `DeviceId` (UUID inmutable) como identidad técnica de flota, separada de `VehicleName` (`VH-###`) y de `VehicleType` (catálogo cerrado).

## Identidad

- **DeviceId**: UUID estable (Kafka, historial, partición)
- **VehicleName**: nombre visible editable (`VH-###` al registrar)
- **VehicleType**: atributo separado (`car`, `motorcycle`, `van`, `truck`, `bus`, `pickup`); default legacy `car`; **no** se infiere desde el nombre

## Backend

- Registro `fleet_devices` + pipeline por `DeviceId`
- Migración PostgreSQL/Timescale **v7** (DeviceId) y **v8** (`vehicle_type` + CHECK)
- `POST /api/devices/register` con `vehicleType` opcional; idempotente **no sobrescribe** tipo existente
- `PATCH /api/devices/{deviceId}/profile` (nombre y/o tipo); `PATCH .../name` por compatibilidad
- Flota y SSE incluyen `vehicleType` desde `fleet_devices` (online y offline)
- Enrolamiento demo `POST /api/auth/device-token` solo con `AllowDemoDeviceEnrollment=true` en Development; **prohibido en Production**

## Mobile

- Cola SQLite **schema v5** con `readyDbPromise`
- Captura fija **cada 5 segundos**; sin selector de frecuencia
- Selector de tipo de vehículo + «Guardar perfil»
- SecureStore `fleet.profile.vehicleType` (legacy → `car`)
- Registro remoto **no** se repite al cambiar perfil (usa `initialVehicleTypeRef`)
- Actualización de perfil **atómica** (restaura nombre/tipo ante cualquier error)
- Sync single-flight con generaciones

## Web / SSE

- Iconos SVG por tipo; label del mapa = solo `vehicleName`
- Popup y «Estado de flota» unificados Demo/API/SSE
- `null` de velocidad → `—`; `0` → `0 km/h`
- Selector visual **solo** 5 / 10 / 15 / 20 s (default **5**)
- Buffer SSE; alertas inmediatas; Actualizar hace flush
- Parches SSE usan `NormalizedVehiclePatch` (sin metadatos en `VehicleStatus`)
- Playwright en CI con `NEXT_PUBLIC_E2E_TEST_MODE`

## Validación

- Application / Worker / Mobile / Web unitarios
- Integration.Tests VehicleType v8 con Testcontainers
- Docker Compose profile `app`
- Playwright E2E (incl. VH-005 motocicleta)

## Base del PR

`feature/device-identity` → **`develop`**

No se realizó merge.
