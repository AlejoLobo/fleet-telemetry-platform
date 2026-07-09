# Fleet Telemetry Platform

Portal corporativo para monitoreo de flotas con telemetría, arquitectura event-driven, IA operativa, dashboard web, app móvil offline-first, pruebas de carga, Docker Compose e infraestructura como código.

**Ruta del proyecto:** `C:\projects\fleet-telemetry-platform`

## Resumen ejecutivo

MVP diseñado para demostrar una vertical funcional completa: conductores envían telemetría (offline-first en mobile), el backend la ingesta vía Kafka, un worker la persiste en TimescaleDB y genera alertas, y un dashboard en tiempo real expone estado de flota, alertas y un agente IA operativo.

## Estado actual: Fase 2 ✅

Pipeline event-driven operativo:

```
POST /api/telemetry → Kafka (telemetry.raw) → Worker → TimescaleDB + alertas
                              ↑ idempotencia por EventId (processed_events)
```

## Stack de Fase 2

| Componente | Tecnología |
|---|---|
| Lenguaje | C# |
| Runtime | .NET 10 LTS (`net10.0`) |
| API | ASP.NET Core Web API |
| Worker | .NET Worker Service |
| Eventos | Kafka (Redpanda en Docker, puerto `19092`) |
| Persistencia | TimescaleDB (PostgreSQL + hypertable) |
| Arquitectura | Clean Architecture (Domain, Application, Infrastructure) |

## Estructura del repositorio

```
fleet-telemetry-platform/
├── .cursorrules
├── backend/
│   ├── FleetTelemetry.sln
│   ├── FleetTelemetry.Api/           # HTTP ingest + health
│   ├── FleetTelemetry.Worker/        # Consumidor Kafka
│   ├── FleetTelemetry.Domain/
│   ├── FleetTelemetry.Application/
│   └── FleetTelemetry.Infrastructure/
├── web/                              # (Fase 4) Dashboard Next.js
├── mobile/                           # (Fase 5) App React Native Expo
├── load-tests/                       # (Fase 6) k6
├── infra/                            # (Fase 6) Terraform AWS
├── docs/
├── docker-compose.yml
├── .env.example
└── README.md
```

## Decisiones técnicas

- **Una sola API** y **un solo Worker** (ver `.cursorrules`).
- **Ingesta desacoplada:** HTTP publica en Kafka; no persiste en controller ni use case.
- **DI por perfil:** `InfrastructureProfile.Api` vs `Worker` en `DependencyInjection.cs`.
- **Fase 2 real:** Kafka publisher + TimescaleDB en Worker.
- **Mocks restantes (API):** flota, alertas lectura, analytics (Druid), IA — hasta Fase 3/4.
- **`DateTimeOffset`** para todas las fechas de telemetría.
- **Validación centralizada** en `TelemetryEventValidator`.

## Endpoints disponibles (Fase 2)

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/health` | Health check |
| `POST` | `/api/telemetry` | Ingesta un evento → publica en Kafka (202 Accepted) |
| `POST` | `/api/telemetry/batch` | Ingesta un lote → publica en Kafka (202 Accepted) |

OpenAPI (solo Development): `http://localhost:5000/openapi/v1.json`

## Infraestructura local (Docker)

```bash
cd C:\projects\fleet-telemetry-platform
docker compose up -d
```

| Servicio | Puerto | Descripción |
|---|---|---|
| Redpanda (Kafka) | `19092` | Topic `telemetry.raw` |
| TimescaleDB | `5432` | DB/user/pass: `fleet` |

> Si `docker` no se reconoce en PowerShell, reinicia la terminal o agrega Docker al PATH.

## Comandos backend

```bash
cd C:\projects\fleet-telemetry-platform\backend

# Compilar (detener API/Worker antes si están corriendo)
dotnet build

# Terminal 1 — API
dotnet run --project FleetTelemetry.Api

# Terminal 2 — Worker
dotnet run --project FleetTelemetry.Worker
```

## Ejemplo end-to-end

```bash
# 1. Ingestar telemetría (genera alertas: speed=130, fuel=10)
curl -X POST http://localhost:5000/api/telemetry \
  -H "Content-Type: application/json" \
  -d "{
    \"eventId\": \"11111111-1111-1111-1111-111111111111\",
    \"vehicleId\": \"VH-001\",
    \"driverId\": \"DRV-001\",
    \"timestamp\": \"2026-07-08T22:00:00Z\",
    \"latitude\": 4.6533,
    \"longitude\": -74.0836,
    \"speedKmh\": 130.0,
    \"fuelLevelPercent\": 10.0,
    \"batteryPercent\": 95.0
  }"
```

Respuesta (`202 Accepted`):

```json
{ "message": "Telemetry event accepted for processing." }
```

Log esperado en API:

```
Published telemetry event 11111111-... for vehicle VH-001 to telemetry.raw partition 0
```

Log esperado en Worker (tras ~1-2 s):

```
Telemetry event processed: 11111111-... vehicle VH-001
Alert generated: overspeed (critical) for vehicle VH-001
Alert generated: low_fuel (warning) for vehicle VH-001
```

## Reglas de alerta (Worker)

| Tipo | Condición | Severidad |
|---|---|---|
| `overspeed` | speedKmh > 120 | critical |
| `low_fuel` | fuelLevelPercent < 15 | warning |
| `low_battery` | batteryPercent < 20 | warning |

## Configuración

Valores en `backend/FleetTelemetry.Api/appsettings.json` y `backend/FleetTelemetry.Worker/appsettings.json`.
Referencia adicional en `.env.example` (no se carga automáticamente; usar convención `Kafka__BootstrapServers` si se prefiere variables de entorno).

## Qué NO está implementado todavía

- Endpoints de lectura (`GET /api/fleet`, `GET /api/alerts`)
- SSE tiempo real
- Agente IA operativo con tools internas
- Dashboard Next.js
- App móvil React Native Expo
- Pruebas de carga k6
- Terraform AWS blueprint
- Druid (solo `MockAnalyticsQueryService`)

## Fase 3 (siguiente)

- Endpoints de lectura (flota, alertas) con TimescaleDB real
- SSE para tiempo real
- Agente IA con tools internas

## Git — ramas y commits de Fase 2

Rama de trabajo: `feature/phase-2-kafka-timescaledb`

| Commit | Tipo | Descripción |
|---|---|---|
| `978b0c0` | chore | Excluir `bin/`/`obj/` del repositorio |
| `f65bd76` | chore | Docker Compose: Redpanda + TimescaleDB |
| `01ffc29` | feat | Kafka publisher, Worker consumer, TimescaleDB, idempotencia, alertas |
| *(pendiente)* | docs | README alineado con Fase 2 |
| *(pendiente)* | chore | Carpetas placeholder + limpieza código muerto |

Para publicar:

```bash
git push -u origin feature/phase-2-kafka-timescaledb
# Abrir PR hacia main en GitHub
```

## AI Audit

| Área | Estado Fase 2 |
|---|---|
| Clean Architecture | ✅ Capas separadas, DI por interfaces |
| Ingesta desacoplada | ✅ API → Kafka; Worker → TimescaleDB |
| Idempotencia | ✅ `processed_events` con `ON CONFLICT DO NOTHING` |
| Event-driven | ✅ Redpanda + topic `telemetry.raw` |
| Mocks acotados | ✅ Solo lectura/analytics/IA en perfil Api |
| Tests automatizados | ❌ Pendiente |
| Seguridad | ❌ Sin autenticación (post-MVP) |
