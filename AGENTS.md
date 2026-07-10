# AGENTS.md

## Cursor Cloud / agentes

Comandos estándar y arquitectura: [README.md](README.md) y [docs/getting-started.md](docs/getting-started.md). Aquí solo caveats no obvios al levantar el pipeline en una VM.

### Orden de arranque

1. **Infra:** `docker compose up -d` → Redpanda (`localhost:19092`) + TimescaleDB (`localhost:5432`, db/user/pass `fleet`). Tópicos: `telemetry.raw`, `telemetry.dead-letter`.
2. **Worker** (`dotnet run --project FleetTelemetry.Worker` en `backend/`): consume Kafka y crea el esquema TimescaleDB al iniciar. Conviene antes que la API.
3. **API** (`dotnet run --project FleetTelemetry.Api`): `http://localhost:5000`.
4. **Web** (`npm run dev` en `web/`): `http://localhost:3000`. Copiar `web/.env.example` → `web/.env.local` si hace falta.

Stack todo-en-uno: `docker compose --profile app up -d --build`. Smoke: `./scripts/smoke-test.ps1` o `bash scripts/smoke-test.sh`.

### Caveats

- **Docker sin systemd:** puede requerirse `sudo dockerd` en sesión persistente antes de `docker compose`.
- **TimescaleDB y cgroups:** si el contenedor queda unhealthy, verificar `docker-compose.override.yml` con `NO_TS_TUNE=true`.
- **Worker al inicio:** `Subscribed topic not available: telemetry.raw` es normal hasta el primer produce / `kafka-init`.
- **Tests:** `dotnet test backend/FleetTelemetry.sln` (Application + Worker + Integration). Web: `npm run build`. Mobile: `npm run typecheck`.
- Reiniciar API/Worker tras cambios NuGet (`dotnet run` no recarga dependencias en caliente).

### Prueba rápida

```bash
curl -X POST http://localhost:5000/api/telemetry -H "Content-Type: application/json" \
  -d '{"eventId":"11111111-1111-1111-1111-111111111111","vehicleId":"VH-001","driverId":"DRV-001","timestamp":"2026-07-09T08:00:00Z","latitude":4.6533,"longitude":-74.0836,"speedKmh":130.0,"fuelLevelPercent":10.0,"batteryPercent":95.0}'
```

Más detalle: [docs/api-and-ops.md](docs/api-and-ops.md), [docs/testing.md](docs/testing.md).
