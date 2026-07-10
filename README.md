# Fleet Telemetry Platform

[![CI](https://github.com/AlejoLobo/fleet-telemetry-platform/actions/workflows/ci.yml/badge.svg)](https://github.com/AlejoLobo/fleet-telemetry-platform/actions/workflows/ci.yml)

Plataforma de monitoreo de flotas con telemetría en tiempo real: ingesta HTTP, pipeline event-driven (Kafka), persistencia en TimescaleDB, dashboard Next.js, app móvil offline-first y agente IA operativo.

## Resumen

Conductores (mobile) o simuladores envían telemetría → la API publica en Kafka → el Worker persiste en TimescaleDB, genera alertas e idempotencia → el dashboard consume API/SSE. MVP vertical completo, defendible en demo y sustentación.

| Capa | Tecnología |
|------|------------|
| API / Worker | .NET 10, ASP.NET Core, Clean Architecture |
| Eventos | Kafka (Redpanda local) |
| Persistencia | TimescaleDB (PostgreSQL + hypertable) |
| Dashboard | Next.js 15 + React 19 |
| Mobile | Expo 52, SQLite offline-first |
| Infra | Docker Compose + Terraform blueprint AWS |

## Arquitectura

```mermaid
flowchart LR
  Mobile[Mobile_Expo] --> Api[API_ASPNET]
  Api -->|produce| Kafka[Kafka_Redpanda]
  Kafka -->|consume| Worker[Worker]
  Worker --> Ts[TimescaleDB]
  Api --> Ts
  Api -->|SSE_polling| Web[Dashboard_Next]
  Web --> Api
```

Flujo principal: `POST /api/telemetry` → `telemetry.raw` → Worker (`TelemetryMessageProcessor`) → `telemetry_events` / `fleet_alerts` / `processed_events`. Detalle en [docs/architecture.md](docs/architecture.md).

## Quickstart

```bash
# Stack completo (Redpanda + TimescaleDB + API + Worker + Web)
docker compose --profile app up -d --build

# Smoke test E2E (API → Kafka → Worker → DB + DLQ)
./scripts/smoke-test.ps1          # Windows
bash scripts/smoke-test.sh        # Bash
```

| Servicio | URL |
|----------|-----|
| API | http://localhost:5000 |
| Dashboard | http://localhost:3000 |
| Kafka (externo) | `localhost:19092` |
| TimescaleDB | `localhost:5432` (user/pass/db: `fleet`) |

Solo infra (API/Worker/Web en host): `docker compose up -d`. Guía completa: [docs/getting-started.md](docs/getting-started.md).

## Endpoints clave

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/health/live` | Liveness |
| `GET` | `/health/ready` | Readiness (DB + Kafka) |
| `GET` | `/api/ops/summary` | Resumen operativo |
| `POST` | `/api/telemetry` | Ingesta → Kafka (`202`) |
| `GET` | `/api/fleet` | Estado de flota |
| `GET` | `/api/alerts` | Alertas abiertas |
| `GET` | `/api/events/stream` | SSE |
| `POST` | `/api/ai/query` | Agente IA |

Lista completa y ejemplos: [docs/api-and-ops.md](docs/api-and-ops.md).

Ver [docs/demo-sustentacion.md](docs/demo-sustentacion.md) para el guion de evaluación y checklist contra requerimientos.

## Documentación

| Guía | Contenido |
|------|-----------|
| [docs/README.md](docs/README.md) | Índice |
| [docs/demo-sustentacion.md](docs/demo-sustentacion.md) | Guion de demo y checklist |
| [docs/architecture.md](docs/architecture.md) | Clean Architecture, DI, flujo |
| [docs/getting-started.md](docs/getting-started.md) | Arranque local, env, caveats |
| [docs/api-and-ops.md](docs/api-and-ops.md) | Endpoints, auth, health/ops |
| [docs/worker-and-dlq.md](docs/worker-and-dlq.md) | Processor, validación, DLQ |
| [docs/testing.md](docs/testing.md) | Unitarios, integración, smoke, CI |
| [docs/realtime-sse.md](docs/realtime-sse.md) | SSE por polling (decisión MVP) |
| [infra/README.md](infra/README.md) | Terraform blueprint AWS |
| [web/README.md](web/README.md) / [mobile/README.md](mobile/README.md) | Frontend y app |

## Estructura del repositorio

```
fleet-telemetry-platform/
├── backend/
│   ├── FleetTelemetry.sln
│   ├── FleetTelemetry.Api/
│   ├── FleetTelemetry.Worker/
│   ├── FleetTelemetry.Domain/
│   ├── FleetTelemetry.Application/
│   ├── FleetTelemetry.Infrastructure/
│   ├── FleetTelemetry.Application.Tests/
│   ├── FleetTelemetry.Worker.Tests/
│   └── FleetTelemetry.Integration.Tests/
├── web/                 # Dashboard Next.js
├── mobile/              # Expo offline-first
├── scripts/             # smoke-test.ps1 / smoke-test.sh
├── load-tests/          # k6
├── infra/terraform/     # Blueprint AWS
├── docs/
├── docker-compose.yml
└── .env.example
```

## Auditoría de IA y criterio arquitectónico

Documentación de propuestas deficientes de IA y correcciones con criterio senior (no confundir con auditoría de paquetes NuGet).

### Caso 1: offsets Kafka

- **Enfoque inicial:** ante `RetryWithoutCommit`, hacer `Task.Delay` y volver a `Consume()`.
- **Riesgo:** no hacer commit no reposiciona el cursor local; consumir el offset N+1 y confirmarlo puede confirmar indirectamente offsets anteriores y perder N.
- **Decisión:** mantener el mismo `ConsumeResult` hasta un resultado terminal (éxito, duplicado, DLQ o apagado), con backoff configurable.
- **Archivos:** `TelemetryConsumerWorker.cs`, `KafkaProcessingRetryBackoff.cs`, `KafkaOptions`.
- **Pruebas:** Worker unitarios + integración Kafka (`Failed_first_offset_is_retried_before_second_offset_is_processed`).
- **Commit:** `e557210` (`fix(worker): garantizar at-least-once reintentando el mismo offset`) y commits posteriores de endurecimiento.

### Caso 2: intentos en memoria

- **Enfoque inicial:** `Dictionary<string, int> _processingAttempts` en el processor.
- **Riesgo:** estado no durable, se pierde al reiniciar, incorrecto con múltiples réplicas, crecimiento potencial.
- **Decisión:** processor stateless; el ciclo del mensaje administra `currentAttempt`.
- **Archivos:** `TelemetryMessageProcessor.cs`.
- **Pruebas:** intentos explícitos en `TelemetryMessageProcessorTests`.

### Caso 3: payload vacío ignorado

- **Enfoque inicial:** `IgnoreWithoutCommit` para payloads vacíos (avance potencial sin DLQ ni commit explícito).
- **Riesgo:** pérdida silenciosa al confirmar offsets posteriores.
- **Decisión:** eliminar `IgnoreWithoutCommit`; null/vacío/whitespace → DLQ `invalid_payload`.
- **Archivos:** `TelemetryMessageProcessingResult.cs`, `TelemetryMessageProcessor.cs`, `TelemetryConsumerWorker.cs`.
- **Pruebas:** payloads nulos, whitespace, JSON `null`, fallo de DLQ.

## Limitaciones MVP (conscientes)

- Terraform es **blueprint** (sin MSK, ALB completo, tasks productivas ni deploy del dashboard). Ver [infra/README.md](infra/README.md).
- Analytics Druid: **no desplegado**; solo contrato `IAnalyticsQueryService` con implementación Timescale. Ver [docs/analytics-druid-mock.md](docs/analytics-druid-mock.md).
- SSE por **polling** a DB (no push Kafka→SSE). Ver [docs/realtime-sse.md](docs/realtime-sse.md).
- JWT opcional y parcial; OpenAI opcional (pulido de texto).
- Preview mobile EAS manual (`mobile-preview.yml`), sin tiendas.
- OpenTelemetry **no implementado** (siguiente paso productivo).
- Worker serial: un mensaje bloqueado puede detener particiones asignadas a la instancia.
- Kafka es **at-least-once**, no exactly-once end-to-end.

## Commits

Mensajes en **español**, conventional commits:

```
tipo(alcance): descripción breve en imperativo
```

Ejemplos: `feat(worker): ...`, `fix(ci): ...`, `docs(readme): ...`, `test(e2e): ...`.
