# Fleet Telemetry Platform

Portal corporativo para monitoreo de flotas con telemetría, arquitectura event-driven, IA operativa, dashboard web, app móvil offline-first, pruebas de carga, Docker Compose e infraestructura como código.

**Ruta del proyecto:** `C:\projects\fleet-telemetry-platform`

## Resumen ejecutivo

MVP diseñado para demostrar una vertical funcional completa: conductores envían telemetría (offline-first en mobile), el backend la ingesta vía Kafka, un worker la persiste en TimescaleDB y genera alertas, y un dashboard en tiempo real expone estado de flota, alertas y un agente IA operativo.

## Estado actual: Fases 4–6 ✅

Pipeline event-driven operativo + lectura, SSE, agente IA, **dashboard Next.js** y **app móvil Expo** offline-first.

```
POST /api/telemetry → Kafka → Worker → TimescaleDB + alertas
GET  /api/fleet, /api/alerts, /api/telemetry/{id}
GET  /api/events/stream (SSE)
POST /api/ai/query (tools internas, sin LLM externo)
POST /api/telemetry/batch (sync mobile)
Dashboard Next.js → http://localhost:3000 (web/)
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
├── web/                              # Dashboard Next.js (Fase 4)
├── mobile/                           # App React Native Expo (Fase 5)
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

## Endpoints disponibles (Fase 3)

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/health` | Health check |
| `POST` | `/api/telemetry` | Ingesta un evento → publica en Kafka (202 Accepted) |
| `POST` | `/api/telemetry/batch` | Ingesta un lote → publica en Kafka (202 Accepted) |
| `GET` | `/api/telemetry/{vehicleId}` | Historial de telemetría (`?from=&to=`) |
| `GET` | `/api/fleet` | Estado actual de todos los vehículos |
| `GET` | `/api/fleet/{vehicleId}` | Estado de un vehículo |
| `GET` | `/api/alerts` | Alertas abiertas |
| `GET` | `/api/events/stream` | SSE tiempo real (fleet-update, alert, heartbeat) |
| `POST` | `/api/ai/query` | Agente IA operativo con tools internas |

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

### Consultar flota y alertas (Fase 3)

```bash
curl http://localhost:5000/api/fleet
curl http://localhost:5000/api/alerts
curl "http://localhost:5000/api/telemetry/VH-001?from=2026-07-08T00:00:00Z"
```

### SSE tiempo real

```bash
curl -N http://localhost:5000/api/events/stream
```

Eventos: `connected`, `fleet-update`, `alert`, `heartbeat`.

### Agente IA operativo

```bash
curl -X POST http://localhost:5000/api/ai/query \
  -H "Content-Type: application/json" \
  -d "{\"question\": \"¿Qué vehículos tienen alertas críticas?\"}"
```

Preguntas soportadas: alertas críticas, vehículos detenidos, exceso de velocidad, estado de `VH-001`, resumen analítico.

## Reglas de alerta (Worker)

| Tipo | Condición | Severidad |
|---|---|---|
| `overspeed` | speedKmh > 120 | critical |
| `low_fuel` | fuelLevelPercent < 15 | warning |
| `low_battery` | batteryPercent < 20 | warning |

## Configuración

Valores en `backend/FleetTelemetry.Api/appsettings.json` y `backend/FleetTelemetry.Worker/appsettings.json`.
Referencia adicional en `.env.example` (no se carga automáticamente; usar convención `Kafka__BootstrapServers` si se prefiere variables de entorno).

## Dashboard web (Fase 4)

```bash
cd web
cp .env.example .env.local   # opcional
npm install
npm run dev
```

| Variable | Descripción |
|----------|-------------|
| `NEXT_PUBLIC_API_URL` | Backend .NET (default `http://localhost:5000`) |
| `NEXT_PUBLIC_USE_MOCK=true` | Datos mock sin backend |

Incluye: mapa de flota, alertas, telemetría, SSE en vivo, chat IA y resumen analítico. Fallback automático a mock si el backend no responde.

Ver `web/README.md` para detalles.

## App móvil (Fase 5)

```bash
cd mobile
cp .env.example .env
npm install
npx expo start
```

Cola SQLite offline, `EventId` en cliente, sync batch al reconectar. Ver `mobile/README.md`.

## Qué NO está implementado todavía

- Despliegue ECS/MSK completo en AWS (blueprint Terraform base listo)
- Login visual en el dashboard (JWT vía `POST /api/auth/login` o token en localStorage)

## Fase 6 ✅

- Pruebas de carga **k6** (`load-tests/`)
- **Terraform** AWS blueprint (`infra/terraform/`)
- **Docker Compose** con API, Worker y Web
- **JWT** opcional (`Auth:Enabled`)
- **Tests** xUnit en `FleetTelemetry.Application.Tests`
- **Ack alertas** `PATCH /api/alerts/{id}/acknowledge`
- **LLM opcional** OpenAI para pulir respuestas del agente IA

## Git y convención de commits

**Idioma:** todos los mensajes de commit y de merge en **español**.

### Formato

```
tipo(alcance): descripción breve en imperativo
```

| Tipo | Cuándo usarlo | Ejemplo |
|------|---------------|---------|
| `feat` | Funcionalidad nueva | `feat(telemetria): implementar consumidor Kafka` |
| `fix` | Corrección de bug | `fix(worker): omitir payloads inválidos en Kafka` |
| `chore` | Mantenimiento, limpieza, config | `chore(infra): agregar Redpanda y TimescaleDB` |
| `docs` | Solo documentación | `docs(readme): documentar convención de commits` |
| `refactor` | Cambio interno sin nueva feature | `refactor(infra): separar perfiles Api y Worker` |
| `test` | Pruebas | `test(carga): agregar simulación k6 de telemetría` |

**Reglas:**
- Descripción en minúsculas después de los dos puntos.
- Imperativo: "agregar", "implementar", "corregir" (no "agregado" ni "agrega").
- Commits pequeños y atómicos: un cambio lógico por commit.
- Ramas: `feature/nombre-fase`, `fix/descripcion-corta`.

### Flujo de ramas

```
main          ← rama estable (GitHub)
develop       ← integración (opcional)
feature/*     ← trabajo por fase
```

### Historial Fase 2 (rama `feature/phase-2-kafka-timescaledb`)

| Commit | Mensaje |
|--------|---------|
| `978b0c0` | `chore(project): excluir artefactos de compilación del repositorio` |
| `f65bd76` | `chore(infra): agregar Redpanda y TimescaleDB para ejecución local` |
| `01ffc29` | `feat(telemetria): implementar procesamiento Kafka y persistencia de eventos` |
| *(local)* | `chore(phase-2): cerrar correcciones de documentación y limpieza` |

### Publicar y fusionar PR

```bash
# 1. Subir rama
git push -u origin feature/phase-2-kafka-timescaledb

# 2. En GitHub: Pull request → base: main, compare: feature/phase-2-kafka-timescaledb
# 3. Fusionar con "Merge pull request" (conserva historial de commits)
```

**Mensaje sugerido al fusionar el PR** (antes de *Confirm merge*):

```
Merge pull request #N: Fase 2 — pipeline Kafka + TimescaleDB
```

Si usas **Squash and merge** (un solo commit en `main`):

```
feat(backend): Fase 2 — pipeline event-driven con Kafka y TimescaleDB

- Docker Compose con Redpanda y TimescaleDB
- API publica en el topic telemetry.raw
- Worker consume, aplica idempotencia por EventId y genera alertas básicas
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
