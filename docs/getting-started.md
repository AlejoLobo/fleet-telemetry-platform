# Getting started

## Opción A — Stack completo en Docker

```bash
docker compose --profile app up -d --build
```

Levanta Redpanda, TimescaleDB, API (`:5000`), Worker y Web (`:3000`).

Validar:

```bash
./scripts/smoke-test.ps1      # Windows
bash scripts/smoke-test.sh    # Bash
curl http://localhost:5000/health/live
curl http://localhost:5000/health/ready
```

## Opción B — Solo infra + procesos en host

```bash
docker compose up -d   # Redpanda + TimescaleDB (+ kafka-init)
```

```bash
cd backend
dotnet run --project FleetTelemetry.Worker   # terminal 1 (crea esquema al iniciar)
dotnet run --project FleetTelemetry.Api      # terminal 2 → :5000
```

```bash
cd web
cp .env.example .env.local   # opcional
npm install && npm run dev   # → :3000
```

Mobile: ver [../mobile/README.md](../mobile/README.md).

## Puertos

| Servicio | Puerto |
|----------|--------|
| API | 5000 |
| Web | 3000 |
| Redpanda (Kafka externo) | 19092 |
| TimescaleDB | 5432 |

Credenciales DB locales: user/password/database `fleet`.

## Variables de entorno

Referencia: [../.env.example](../.env.example). Convención ASP.NET: `Section__Key`.

| Variable | Notas |
|----------|-------|
| `TimescaleDb__ConnectionString` | Obligatorio en runtime |
| `Kafka__BootstrapServers` | Local: `localhost:19092`; Compose app: `redpanda:9092` |
| `Kafka__DeadLetterTopic` | Default `telemetry.dead-letter` |
| `Kafka__MaxProcessingAttempts` | Intentos del mismo offset antes de DLQ (default `3`) |
| `Kafka__RetryInitialDelayMilliseconds` | Backoff inicial (default `500`) |
| `Kafka__RetryMaxDelayMilliseconds` | Tope de backoff (default `5000`) |
| `Kafka__MaxDeadLetterPublishAttempts` | Fallos DLQ antes de detener Worker (default `5`) |
| `Kafka__MaxPollIntervalMilliseconds` | `MaxPollIntervalMs` (default `300000`) |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Defaults locales de desarrollo (`fleet`) |
| `TIMESCALEDB_CONNECTION_STRING` | Connection string Compose (default hacia servicio `timescaledb`) |
| `Auth__Enabled` | Default `false` |
| `Auth__JwtSecret` / `Auth__DemoPassword` | Obligatorios si Auth habilitado (≥ 32 chars el secret) |
| `OpenAI__ApiKey` | Opcional |
| `Sse__ActivePollIntervalSeconds` / `Idle...` | Default 3 / 10 |
| `FLEET_API_URL` | Smoke tests (default `http://localhost:5000`) |
| `FLEET_INTEGRATION_DB_CONNECTION` | Tests de integración sin Testcontainers |

`appsettings.json` no debe contener secretos reales. Validación al arranque: `ConfigurationValidator`.

## Caveats

Detalles de VMs / Docker sin systemd / Timescale cgroups: [dev-environment.md](dev-environment.md).

- Si `docker` no está en PATH en PowerShell, reinicia la terminal.
- En Compose, el Worker depende de la API solo para orden de arranque; el esquema lo inicializa el Worker.
- Tras cambios NuGet, reinicia API/Worker (`dotnet run` no recarga paquetes en caliente).
