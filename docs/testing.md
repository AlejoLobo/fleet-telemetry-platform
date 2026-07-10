# Tests y CI

## Proyectos

| Proyecto | Tipo | Qué cubre |
|----------|------|-----------|
| `FleetTelemetry.Application.Tests` | Unitario | Validadores, alertas, IA parser, health/ops controllers, circuit breakers (~36) |
| `FleetTelemetry.Worker.Tests` | Unitario | `TelemetryMessageProcessor`: DLQ, commit semantics, reintentos (~7) |
| `FleetTelemetry.Integration.Tests` | Integración | Idempotencia, transacción, overspeed, payload inválido vs TimescaleDB (~4) |

## Comandos

```bash
cd backend
dotnet test --configuration Release

# Solo un proyecto
dotnet test FleetTelemetry.Application.Tests --configuration Release
dotnet test FleetTelemetry.Worker.Tests --configuration Release
dotnet test FleetTelemetry.Integration.Tests --configuration Release
```

Web / mobile (también en CI):

```bash
npm ci --prefix web && npm run build --prefix web
npm ci --prefix mobile && npm run typecheck --prefix mobile
```

## Integración — base de datos

**Por defecto:** Testcontainers (`timescale/timescaledb:latest-pg16`). Requiere Docker.

**Sin Testcontainers:** TimescaleDB de Compose:

```bash
docker compose up -d timescaledb

# PowerShell
$env:FLEET_INTEGRATION_DB_CONNECTION="Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet"
dotnet test backend/FleetTelemetry.Integration.Tests --configuration Release
```

## Smoke test E2E

Asume `docker compose --profile app up -d --build`.

```bash
./scripts/smoke-test.ps1
bash scripts/smoke-test.sh
```

Valida: API viva → evento válido procesado en flota → payload inválido en `telemetry.dead-letter` (`invalid_payload`). No inserta en DB a mano.

## CI (GitHub Actions)

Workflow: [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)

| Job | Pasos |
|-----|-------|
| Backend | restore → build Release → `dotnet test` (solución) → auditoría vulnerabilidades |
| Web | `npm ci` → `npm run build` |
| Mobile | `npm ci` → `npm run typecheck` |

.NET 10: `include-prerelease: true` + [global.json](../global.json).

Adicionales:

- `mobile-ci.yml` — typecheck en cambios de `mobile/`
- `mobile-preview.yml` — EAS APK manual (`workflow_dispatch`, secret `EXPO_TOKEN`)

Sin despliegue productivo automático en push/PR.
