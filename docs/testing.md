# Tests y CI

## Proyectos

| Proyecto | Tipo | Qué cubre |
|----------|------|-----------|
| `FleetTelemetry.Application.Tests` | Unitario | Validadores, alertas, IA, health/ops, `DatabaseTransientFailureClassifier` |
| `FleetTelemetry.Worker.Tests` | Unitario | Processor (payloads vacíos→DLQ), backoff acotado, sesión DLQ |
| `FleetTelemetry.Integration.Tests` | Integración | TimescaleDB (idempotencia/transacción) + Kafka real (offsets, redelivery, DLQ) |

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

## Integración — TimescaleDB

Imagen fija: `timescale/timescaledb:2.17.2-pg16`.

**Por defecto:** Testcontainers (requiere Docker).

**Sin Testcontainers:**

```bash
docker compose up -d timescaledb
$env:FLEET_INTEGRATION_DB_CONNECTION="Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet"
dotnet test backend/FleetTelemetry.Integration.Tests --configuration Release
```

## Integración — Kafka

Testcontainers Kafka (`Testcontainers.Kafka`). Prueba obligatoria: `Failed_first_offset_is_retried_before_second_offset_is_processed` (mismo partition, reintento de A antes de B, committed offset, sin redelivery tras reinicio).

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
