# Changelog

Todos los cambios relevantes de este proyecto se documentan en este archivo.

El formato está basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto utiliza [Semantic Versioning](https://semver.org/lang/es/).

## [Unreleased]

## [1.0.0] - 2026-07-17

Primera versión estable y demostrable de Fleet Telemetry Platform: vertical
completa de telemetría, dashboard en tiempo real, móvil offline-first e
infraestructura AWS **dev** reproducible. No declara alta disponibilidad
productiva ni exactly-once end-to-end.

### Added

- Pipeline de telemetría HTTP → Kafka/Redpanda → Worker → TimescaleDB.
- App móvil Expo offline-first con cola SQLite y sync batch.
- Dashboard Next.js con mapa, flota, alertas, telemetría y chat IA.
- Registro e identidad técnica estable mediante `DeviceId` (UUID).
- `VehicleName` y `VehicleType` (catálogo cerrado) separados de la identidad.
- Alertas operativas con deduplicación de condiciones activas y cooldown.
- SSE **KafkaPush** con replay, `Last-Event-ID`, `stream-reset` y resync.
- Agente IA operativo con tools internas controladas (OpenAI opcional).
- Infraestructura AWS **dev** reproducible con Terraform (EC2 + Compose).
- OpenTelemetry opt-in (OTLP; sin collector incluido en el monorepo).
- Pruebas unitarias, integración (Kafka/TimescaleDB), E2E Playwright, smoke,
  k6 reducido, builds Docker y validación Terraform en CI.

### Changed

- Migración del móvil a Expo SDK 54.
- Separación explícita entre `DeviceId`, `VehicleName` y `VehicleType`.
- Sustitución del polling como modo SSE predeterminado por KafkaPush.
- Endurecimiento del contrato Kafka y del read model de flota.
- Versionado unificado del producto a `1.0.0` (backend, web y mobile).

### Fixed

- Reutilización del mismo offset Kafka durante retries (sin avance prematuro).
- Errores inesperados del Worker: sin DLQ, sin commit y con detención controlada.
- Consistencia del read model y paginación por cursor.
- Autenticación SSE cuando `Auth:Enabled=true`.
- Estabilización de CI, integración Kafka/TimescaleDB y Terraform **dev**.

### Security

- JWT configurable y autorización por políticas.
- Rate limiting y CORS configurables.
- Secrets Manager, IAM y SSM en el entorno AWS **dev**.
- Servicios de datos (Kafka/TimescaleDB) no expuestos públicamente en Terraform **dev**.

[Unreleased]: https://github.com/AlejoLobo/fleet-telemetry-platform/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/AlejoLobo/fleet-telemetry-platform/releases/tag/v1.0.0
