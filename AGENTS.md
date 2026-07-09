# AGENTS.md

## Cursor Cloud specific instructions

Fleet Telemetry Platform. Comandos estándar (build/run) están en `README.md` y `web/README.md`; abajo solo van los detalles no obvios para levantar los servicios en la VM.

### Servicios y orden de arranque

El pipeline es event-driven, así que el orden importa:

1. **Infra (Docker):** `sudo docker compose up -d` → Redpanda (Kafka, `localhost:19092`, topic `telemetry.raw`) + TimescaleDB (`localhost:5432`, db/user/pass `fleet`).
2. **Worker** (`dotnet run --project FleetTelemetry.Worker` en `backend/`): consume Kafka y **crea el esquema de TimescaleDB al iniciar** (hypertable + tablas). Conviene levantarlo antes que la API para inicializar la base.
3. **API** (`dotnet run --project FleetTelemetry.Api` en `backend/`): escucha en `http://localhost:5000` (perfil `http`, es el default de `dotnet run`).
4. **Web** (`npm run dev` en `web/`): dashboard Next.js en `http://localhost:3000`. Requiere `web/.env.local` (copiar de `web/.env.example`); con `NEXT_PUBLIC_USE_MOCK=false` consume la API real.

### Caveats no obvios

- **Docker no usa systemd en la VM.** El daemon debe arrancarse manualmente (p. ej. `sudo dockerd` en una sesión tmux persistente) antes de `docker compose`.
- **TimescaleDB y cgroups:** las VMs de Cursor Cloud no exponen `/sys/fs/cgroup/memory.max`, por lo que `timescaledb-tune` crashea y el contenedor no arranca. Se resuelve con `docker-compose.override.yml` (en la raíz) que fija `NO_TS_TUNE=true`. Docker Compose lo mergea automáticamente. **Si este override no está presente** (p. ej. el PR de setup no se fusionó), recréalo antes de `docker compose up` o TimescaleDB quedará en estado unhealthy/exited.
- **Worker log transitorio:** al iniciar sin eventos previos aparece `Subscribed topic not available: telemetry.raw`. Es normal: Redpanda (modo dev-container) crea el topic en el primer publish de la API. Desaparece tras el primer `POST /api/telemetry`.
- **No hay proyecto de tests automatizados** en el backend; la verificación es `dotnet build` + prueba end-to-end vía `curl`/dashboard. La web usa `npm run lint` y `npm run build`.
- Si se reinstalan dependencias mientras la API/Worker corren, reiniciar esos procesos (`dotnet run` no recompila en caliente cambios de paquetes NuGet).

### Prueba end-to-end rápida

```bash
curl -X POST http://localhost:5000/api/telemetry -H "Content-Type: application/json" \
  -d '{"eventId":"11111111-1111-1111-1111-111111111111","vehicleId":"VH-001","driverId":"DRV-001","timestamp":"2026-07-09T08:00:00Z","latitude":4.6533,"longitude":-74.0836,"speedKmh":130.0,"fuelLevelPercent":10.0,"batteryPercent":95.0}'
# Luego: GET /api/fleet, /api/alerts, /api/telemetry/VH-001, POST /api/ai/query
```
