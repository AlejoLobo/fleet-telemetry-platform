# Tests y CI

## Proyectos

| Proyecto | Tipo | Qué cubre |
|----------|------|-----------|
| `FleetTelemetry.Application.Tests` | Unitario | Validadores, alertas, IA, health/ops, `DatabaseTransientFailureClassifier` |
| `FleetTelemetry.Worker.Tests` | Unitario | Processor (payloads vacíos→DLQ), backoff acotado, sesión DLQ |
| `FleetTelemetry.Integration.Tests` | Integración | TimescaleDB (idempotencia/transacción/concurrencia de esquema) + Kafka real (offsets, redelivery, DLQ productiva y doubles) |
| `web` (Vitest + Testing Library) | Unitario / integración de componentes | Hooks de datos (`use-fleet-data.test.tsx`), SSE (`use-sse-stream.test.tsx`, `sse-fetch-client.test.ts`, `sse-parser.test.ts`, `sse-reconnect.test.ts`), replay/`Last-Event-ID`, `stream-reset`/resync (`sse-resync.test.ts`, `dashboard-sse-resync.test.tsx`), paginación (`fleet-pagination.test.ts`), cobertura V8 en `test:ci` |
| `mobile` (Jest + jest-expo) | Unitario / integración con SQLite | Auth y SecureStore (`auth-service.test.ts`, `auth-expiration*.test.ts`), `telemetry-api.test.ts`, cola SQLite (`offline-queue.test.ts`), sync batch/fallback (`offline-sync-*.test.ts`, `sync-policy.test.ts`), reanudación post-login (`resume-sync.test.ts`, `use-driver-telemetry-resume.test.ts`), location provider (`location-provider.test.ts`), cobertura en `test:ci` |

## Comandos

```bash
dotnet restore backend/FleetTelemetry.sln
dotnet build backend/FleetTelemetry.sln --configuration Release --no-restore
dotnet test backend/FleetTelemetry.Application.Tests --configuration Release --no-build
dotnet test backend/FleetTelemetry.Worker.Tests --configuration Release --no-build
dotnet test backend/FleetTelemetry.Integration.Tests --configuration Release --no-build
```

Web:

```bash
npm ci --prefix web
npm run lint --prefix web
npm run typecheck --prefix web
npm run test:ci --prefix web
npm run build --prefix web
```

Mobile:

```bash
npm ci --prefix mobile
npm run typecheck --prefix mobile
npm run test:ci --prefix mobile
npm run export --prefix mobile
```

`npm run test:ci --prefix web` ejecuta Vitest con cobertura V8 (`text` + `json-summary`).
`npm run test:ci --prefix mobile` ejecuta Jest con cobertura (`text` + `json-summary`) sobre `src/services`, `src/db` y `src/hooks`.

## Integración — Kafka

**Broker:** servicio Kafka en CI (`confluent-local:7.6.1`), Redpanda local (`localhost:19092`) o Testcontainers si no hay variable de entorno.

```bash
export FLEET_INTEGRATION_KAFKA_BOOTSTRAP=localhost:19092
dotnet test backend/FleetTelemetry.Integration.Tests --configuration Release
```

### Publisher DLQ

| Escenario | Implementación | Qué valida |
|-----------|----------------|------------|
| Payload inválido / whitespace → DLQ real | `KafkaDeadLetterPublisher` vía `UseProductionDeadLetterPublisher = true` | JSON camelCase en tópico real, clave `topic:partition:offset`, commit tras existir mensaje DLQ |
| `KafkaDeadLetterPublisher` directo | Publisher productivo aislado | Serialización, clave Kafka, circuit breaker → `DeadLetterPublishException` |
| Fallos DLQ / redelivery / agotamiento | `ControllableDeadLetterPublisher` | Simula fallos sin depender de broker; lista en memoria + contador de intentos |
| Orden offsets / reintentos | `ControllableTelemetryProcessingUnitOfWork` + worker real | A1→A2→B1, committed offset, redelivery |

Prueba obligatoria de orden: `Failed_first_offset_is_retried_before_second_offset_is_processed`.

## Integración — TimescaleDB

Imagen fija: `timescale/timescaledb:2.17.2-pg16`.

**Por defecto:** Testcontainers (requiere Docker) o servicio en CI.

**Sin Testcontainers:**

```bash
docker compose up -d timescaledb
export FLEET_INTEGRATION_DB_CONNECTION="Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet"
dotnet test backend/FleetTelemetry.Integration.Tests --configuration Release
```

### Inicialización concurrente del esquema

`DatabaseInitializer` adquiere `pg_advisory_lock(742001)` en la **misma conexión** durante todo el DDL y lo libera en `finally` con token no cancelado.

Prueba: `Concurrent_initialization_completes_without_errors_and_leaves_single_schema` ejecuta 8 inicializaciones en paralelo (`Task.WhenAll`) y verifica extensión, tablas, hypertable, índices y ausencia de advisory locks activos.

Las pruebas de integración desactivan paralelismo xUnit por estabilidad del esquema compartido; la prueba de concurrencia valida el lock explícitamente.

`TelemetryProcessingIntegrationTests` valida idempotencia, alertas y consistencia transaccional contra TimescaleDB real.

FT-004 (read model + paginación): `FleetVehicleStateIntegrationTests`, `FleetPaginationIntegrationTests`, `TelemetryHistoryPaginationIntegrationTests`, `OpsAggregateIntegrationTests` (TimescaleDB real). Web: `web/src/lib/fleet-pagination.test.ts`.

## Smoke / k6

```bash
./scripts/smoke-test.ps1
k6 run load-tests/telemetry-ingest.js
# Flota: 1 VU ≈ 1 dispositivo, 1 POST cada 3 s
k6 run -e DEVICES=100 -e INTERVAL_SECONDS=3 -e DURATION=60s load-tests/telemetry-fleet-3s.js
```

## CI

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml):

| Job | Pasos |
|-----|-------|
| Documentation | Decisiones arquitectónicas en README + consistencia test/docs (Web/Mobile `test:ci`, KafkaPush, OpenTelemetry opt-in, entorno AWS `dev`) |
| Backend | restore → build → Application / Worker / Integration tests → audit High/Critical en Api+Worker+Infrastructure+Application |
| Infra | `terraform fmt/validate` (blueprint + `dev`) → rechazo de placeholders/secretos → `docker compose config` |
| Web | `npm ci` → lint → typecheck → `npm run test:ci` (Vitest + cobertura V8) → build |
| Mobile | `npm ci` → typecheck → `npm run test:ci` (Jest + cobertura) → `expo export` |
| Docker images | build de imágenes API/Worker/Web |
| E2E smoke + k6 | stack Compose + smoke bash + k6 reducido |

EAS preview sigue siendo workflow manual.
