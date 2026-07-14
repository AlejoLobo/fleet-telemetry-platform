# AGENTS.md

## Cursor Cloud specific instructions

This is a fleet telemetry monorepo. See `README.md` and `docs/getting-started.md` for the
canonical run/build/test commands; only the non-obvious, environment-specific caveats are
captured here so they are not repeated.

### Components (at a glance)

| Component | Path | Runs on | Standard commands (source) |
|-----------|------|---------|----------------------------|
| Backend API (`net10.0`) | `backend/FleetTelemetry.Api` | :5000 | `docs/getting-started.md`, `.github/workflows/ci.yml` |
| Backend Worker (`net10.0`) | `backend/FleetTelemetry.Worker` | no port | `docs/getting-started.md` |
| Web dashboard (Next.js 15) | `web/` | :3000 | `web/package.json` scripts + `ci.yml` |
| Mobile (Expo 52) | `mobile/` | Metro | `mobile/package.json` scripts + `mobile-ci.yml` |
| Infra: Redpanda + TimescaleDB | `docker-compose.yml` | :19092 / :5432 | `docker compose up -d` |

### Preinstalled by the VM snapshot (do NOT reinstall in the update script)

- **.NET 10 SDK** `10.0.100` at `/usr/share/dotnet`, symlinked to `/usr/local/bin/dotnet` (already on PATH; `global.json` pins this version).
- **Docker Engine + compose plugin**. The daemon is configured with the `fuse-overlayfs`
  storage driver and `containerd-snapshotter` disabled (`/etc/docker/daemon.json`) — required
  because this VM's kernel lacks full overlay2 support. Do not change this.
- **Node 22** + npm (matches CI).
- The `ubuntu` user is in the `docker` group, so new shells can run `docker` without `sudo`.

The update script only refreshes project dependencies (`dotnet restore`, `npm ci`).

### Starting services (not done by the update script)

1. **Start the Docker daemon first** — it is NOT auto-started on a fresh boot:
   `sudo service docker start` (verify with `docker info`).
2. **Bring up infra:** `docker compose up -d` (Redpanda + TimescaleDB + one-shot `kafka-init`
   that creates the `telemetry.raw` and `telemetry.dead-letter` topics). The committed
   `docker-compose.override.yml` sets `NO_TS_TUNE=true`, which is required so TimescaleDB starts
   on cloud VMs without cgroup autotuning failures — keep it.
3. **Run backend on the host in dev mode** (preferred over the `app` Docker profile for
   development). Export these env vars, then run each in its own shell/tmux session:
   - `Kafka__BootstrapServers=localhost:19092` (host mode uses the external `19092` listener;
     in-container services use `redpanda:9092`).
   - `TimescaleDb__ConnectionString=Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet`
   - Worker: `DOTNET_ENVIRONMENT=Development dotnet run --project FleetTelemetry.Worker`
   - API: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project FleetTelemetry.Api`
   - Web: `cd web && cp .env.example .env.local && npm run dev`
4. **Start the Worker before expecting persistence.** The Worker (not the API) initializes the
   TimescaleDB schema on startup, and only in `Development` (DDL auto-creation is disabled in
   Production — see `docs/database-migrations.md`).

### Non-obvious caveats

- **API is ingest-only via Kafka:** `POST /api/telemetry` returns `202` and publishes to Kafka;
  it does not persist directly. Persistence happens asynchronously in the Worker, so allow a
  moment before querying `/api/fleet/{vehicleId}`.
- **`dotnet run` does not hot-reload NuGet changes** — restart API/Worker after package changes.
- **TimescaleDB column names are quoted PascalCase** (e.g. `"VehicleId"`, `"CapturedAt"` on
  `telemetry_events`). Use quoted identifiers in raw SQL.
- **Integration tests need running infra** and env vars (they do not use Testcontainers):
  `FLEET_INTEGRATION_KAFKA_BOOTSTRAP=localhost:19092` and
  `FLEET_INTEGRATION_DB_CONNECTION=Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet`.
- **Restore/build emits `NU1902` (moderate) warnings** for OpenTelemetry packages — expected,
  non-blocking.
- **Web runs in real-backend mode** when `NEXT_PUBLIC_API_URL` is set (`.env.local`); it also has
  a mock mode so the dashboard renders even if the backend is down.
- **AI agent** works without an OpenAI key (mock/tool mode); it only answers scoped operational
  questions and declines anything else. Set `OpenAI__ApiKey` to enable LLM text polishing.
- **Full stack alternative:** `docker compose --profile app up -d --build` runs API + Worker + Web
  in containers; `bash scripts/smoke-test.sh` validates the full E2E pipeline including the DLQ.
