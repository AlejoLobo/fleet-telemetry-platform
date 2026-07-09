# AGENTS.md

## Cursor Cloud

Comandos estándar (build, run, endpoints) están en `README.md` y `web/README.md`. Aquí solo detalles no obvios para levantar el pipeline en la VM.

### Orden de arranque

1. **Infra (Docker):** `docker compose up -d` → Redpanda (`localhost:19092`, topic `telemetry.raw`) + TimescaleDB (`localhost:5432`, db/user/pass `fleet`).
2. **Worker** (`dotnet run --project FleetTelemetry.Worker` en `backend/`): consume Kafka y crea el esquema de TimescaleDB al iniciar. Conviene levantarlo antes que la API.
3. **API** (`dotnet run --project FleetTelemetry.Api` en `backend/`): `http://localhost:5000`.
4. **Web** (`npm run dev` en `web/`): `http://localhost:3000`. Copiar `web/.env.example` → `web/.env.local` si hace falta.

### Caveats

- **Docker sin systemd:** en la VM puede requerirse `sudo dockerd` en una sesión persistente antes de `docker compose`.
- **TimescaleDB y cgroups:** si el contenedor queda unhealthy, verificar que exista `docker-compose.override.yml` con `NO_TS_TUNE=true` (Docker Compose lo mergea automáticamente).
- **Worker al inicio:** `Subscribed topic not available: telemetry.raw` es normal hasta el primer `POST /api/telemetry`.
- **Tests:** `dotnet test backend/FleetTelemetry.Application.Tests`; web: `npm run lint` y `npm run build`.
- Reiniciar API/Worker tras cambios de paquetes NuGet (`dotnet run` no recarga dependencias en caliente).

### Prueba rápida

```bash
curl -X POST http://localhost:5000/api/telemetry -H "Content-Type: application/json" \
  -d '{"eventId":"11111111-1111-1111-1111-111111111111","vehicleId":"VH-001","driverId":"DRV-001","timestamp":"2026-07-09T08:00:00Z","latitude":4.6533,"longitude":-74.0836,"speedKmh":130.0,"fuelLevelPercent":10.0,"batteryPercent":95.0}'
```
