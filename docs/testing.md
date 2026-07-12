# Tests y CI

## Proyectos

| Proyecto | Tipo | Qué cubre |
|----------|------|-----------|
| `FleetTelemetry.Application.Tests` | Unitario | Validadores, alertas, IA, health/ops, `DatabaseTransientFailureClassifier` |
| `FleetTelemetry.Worker.Tests` | Unitario | Processor (payloads vacíos→DLQ), backoff acotado, sesión DLQ |
| `FleetTelemetry.Integration.Tests` | Integración | TimescaleDB (idempotencia/transacción/concurrencia de esquema) + Kafka real (offsets, redelivery, DLQ productiva y doubles) |

## Comandos

```bash
dotnet restore backend/FleetTelemetry.sln
dotnet build backend/FleetTelemetry.sln --configuration Release --no-restore
dotnet test backend/FleetTelemetry.Application.Tests --configuration Release --no-build
dotnet test backend/FleetTelemetry.Worker.Tests --configuration Release --no-build
dotnet test backend/FleetTelemetry.Integration.Tests --configuration Release --no-build
```

Web / mobile:

```bash
npm ci --prefix web && npm run build --prefix web
npm ci --prefix mobile && npm run typecheck --prefix mobile && npm run export --prefix mobile
```

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

FT-004 (read model + paginación): `FleetVehicleStateIntegrationTests`, `FleetPaginationIntegrationTests`, `TelemetryHistoryPaginationIntegrationTests`, `OpsAggregateIntegrationTests` (32 escenarios con TimescaleDB real). Web: `web/src/lib/fleet-pagination.test.ts`.

## Smoke / k6

```bash
./scripts/smoke-test.ps1
k6 run load-tests/telemetry-ingest.js
```

## CI

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml):

| Job | Pasos |
|-----|-------|
| Backend | restore → build → Application / Worker / Integration tests → audit High/Critical en Api+Worker+Infrastructure+Application |
| Web | `npm ci` → `npm run build` |
| Mobile | `npm ci` → typecheck → `expo export --platform android` |

EAS preview sigue siendo workflow manual.
