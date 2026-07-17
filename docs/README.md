# Documentación — Fleet Telemetry Platform

Índice para demo, onboarding y sustentación técnica. El [README](../README.md) es la landing; aquí está el detalle.

| Guía | Descripción |
|------|-------------|
| [demo-sustentacion.md](demo-sustentacion.md) | Guion ejecutivo de demo y checklist de requerimientos |
| [architecture.md](architecture.md) | Clean Architecture, flujo event-driven, DI Api/Worker |
| [getting-started.md](getting-started.md) | Arranque local (Compose, backend, web, mobile) |
| [api-and-ops.md](api-and-ops.md) | Endpoints, auth, health checks y resumen operativo |
| [worker-and-dlq.md](worker-and-dlq.md) | Consumidor Kafka, validación de dominio, DLQ |
| [testing.md](testing.md) | Unitarios, integración, smoke E2E, CI |
| [realtime-sse.md](realtime-sse.md) | SSE KafkaPush (predeterminado) y Polling (alternativa) |
| [database-migrations.md](database-migrations.md) | Migraciones TimescaleDB / esquema de flota |
| [timescaledb-operations.md](timescaledb-operations.md) | Compresión, retención y agregados |
| [kafka-telemetry-contract.md](kafka-telemetry-contract.md) | Contrato de eventos `telemetry.raw` |
| [mobile-sqlite-migration.md](mobile-sqlite-migration.md) | Migración de esquema SQLite móvil |
| [analytics-druid-mock.md](analytics-druid-mock.md) | Contrato analytics vs implementación Timescale |
| [releases/v1.0.0.md](releases/v1.0.0.md) | Notas formales de la versión 1.0.0 |
| [releases/release-checklist.md](releases/release-checklist.md) | Checklist operativo de release |
| [../infra/README.md](../infra/README.md) | Terraform blueprint AWS y root `dev` |
| [../web/README.md](../web/README.md) | Dashboard Next.js |
| [../mobile/README.md](../mobile/README.md) | App Expo + EAS preview |
| [dev-environment.md](dev-environment.md) | Caveats de entorno en VM / desarrollo local |
| [../.env.example](../.env.example) | Variables de entorno de ejemplo |
| [pr-45-description.md](pr-45-description.md) | Descripción histórica del PR #45 (no es runbook) |
