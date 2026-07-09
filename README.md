# Fleet Telemetry Platform

Portal corporativo para monitoreo de flotas con telemetría, arquitectura event-driven, IA operativa, dashboard web, app móvil offline-first, pruebas de carga, Docker Compose e infraestructura como código.

**Ruta del proyecto:** `C:\projects\fleet-telemetry-platform`

## Resumen ejecutivo

MVP diseñado para demostrar una vertical funcional completa: conductores envían telemetría (offline-first en mobile), el backend la ingesta vía Kafka, un worker la persiste en TimescaleDB y genera alertas, y un dashboard en tiempo real expone estado de flota, alertas y un agente IA operativo.

## Estado actual: Fase 2

Pipeline event-driven operativo: la API publica en **Kafka** (`telemetry.raw`), el **Worker** consume, aplica **idempotencia por EventId** y persiste en **TimescaleDB** con alertas básicas.

## Stack de Fase 2

| Componente | Tecnología |
|---|---|
| Lenguaje | C# |
| Runtime | .NET 10 LTS (`net10.0`) |
| API | ASP.NET Core Web API |
| Worker | .NET Worker Service |
| Eventos | Kafka (Redpanda en Docker) |
| Persistencia | TimescaleDB (PostgreSQL + hypertable) |
| Arquitectura | Clean Architecture (Domain, Application, Infrastructure) |

## Estructura del repositorio

```
fleet-telemetry-platform/
├── 
├── backend/
│   ├── FleetTelemetry.sln
│   ├── FleetTelemetry.Api/
│   ├── FleetTelemetry.Domain/
│   ├── FleetTelemetry.Application/
│   ├── FleetTelemetry.Infrastructure/
│   └── FleetTelemetry.Worker/
├── web/                              # (Fase 4) Dashboard Next.js
├── mobile/                           # (Fase 5) App React Native Expo
├── load-tests/                       # (Fase 6) k6
├── infra/                            # (Fase 6) Terraform AWS
├── docs/
├── docker-compose.yml
├── .env.example
└── README.md
```

## Decisiones técnicas (Fase 1)

- **Una sola API** (`FleetTelemetry.Api`) y **un solo Worker** (`FleetTelemetry.Worker`).
- **Ingesta desacoplada:** HTTP publica eventos vía `ITelemetryEventPublisher`; no persiste en controller ni use case.
- **Mocks en Infrastructure** para Kafka, TimescaleDB, Druid (`IAnalyticsQueryService`) e IA (`IAiAgentService`).
- **`DateTimeOffset`** para todas las fechas de telemetría.
- **Validación centralizada** en `TelemetryEventValidator`.
- Sin Redis, sin Testcontainers, sin múltiples APIs.

## Endpoints disponibles

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/health` | Health check |
| `POST` | `/api/telemetry` | Ingesta un evento de telemetría (202 Accepted) |
| `POST` | `/api/telemetry/batch` | Ingesta un lote de eventos (202 Accepted) |

Swagger UI disponible en Development: `http://localhost:5000/swagger`

## Comandos para levantar infraestructura (Docker)

```bash
cd C:\projects\fleet-telemetry-platform
docker compose up -d
```

Servicios:
| Servicio | Puerto | Descripción |
|---|---|---|
| Redpanda (Kafka) | `19092` | Topic `telemetry.raw` |
| TimescaleDB | `5432` | Base de datos `fleet` |

> **Nota:** Si `docker` no se reconoce en PowerShell, reinicia la terminal o agrega `C:\Program Files\Docker\Docker\resources\bin` al PATH.

## Comandos para compilar

```bash
cd C:\projects\fleet-telemetry-platform\backend
dotnet build
```

## Comandos para correr la API

```bash
cd C:\projects\fleet-telemetry-platform\backend
dotnet run --project FleetTelemetry.Api
```

## Comandos para correr el Worker

```bash
cd C:\projects\fleet-telemetry-platform\backend
dotnet run --project FleetTelemetry.Worker
```

## Ejemplo: POST /api/telemetry

```bash
curl -X POST http://localhost:5000/api/telemetry \
  -H "Content-Type: application/json" \
  -d "{
    \"eventId\": \"11111111-1111-1111-1111-111111111111\",
    \"vehicleId\": \"VH-001\",
    \"driverId\": \"DRV-001\",
    \"timestamp\": \"2026-07-08T22:00:00Z\",
    \"latitude\": 4.6533,
    \"longitude\": -74.0836,
    \"speedKmh\": 45.5,
    \"fuelLevelPercent\": 72.0,
    \"batteryPercent\": 95.0
  }"
```

Respuesta esperada (`202 Accepted`):

```json
{
  "message": "Telemetry event accepted for processing."
}
```

En los logs de la API deberías ver:

```
[MOCK] Published telemetry event 11111111-1111-1111-1111-111111111111 for vehicle VH-001
```

## Qué NO está implementado todavía

- Endpoints de lectura (flota, alertas, SSE)
- Agente IA operativo real con tools internas
- Dashboard Next.js
- App móvil React Native Expo
- Pruebas de carga k6
- Terraform AWS blueprint

## Qué se implementará en Fase 3

- **Endpoints de lectura** (estado de flota, alertas)
- **SSE** para tiempo real
- **Agente IA** con tools internas (modo mock o provider configurable)

## AI Audit

Auditoría de decisiones donde la IA inicialmente podría haber tomado atajos incorrectos, y la corrección aplicada con criterio senior.

### 1. Persistencia directa desde controllers

| | |
|---|---|
| **Riesgo IA** | Guardar telemetría directamente en TimescaleDB desde `TelemetryController`, acoplando HTTP con persistencia. |
| **Corrección** | Controller → `IngestTelemetryEventUseCase` → `ITelemetryEventPublisher.PublishAsync`. La persistencia queda para el Worker en Fase 2 vía Kafka. |
| **Estado** | ✅ Alineado con `` |

### 2. Envío de datasets completos al LLM

| | |
|---|---|
| **Riesgo IA** | Enviar historial completo de telemetría al LLM para responder preguntas operativas. |
| **Corrección** | `IAiAgentService` con `MockAiAgentService` en Fase 1. En fases posteriores, el agente usará tools internas (`GetLatestVehicleStatus`, `GetVehiclesAboveSpeed`, etc.) que consultan Application Services, no datasets crudos. |
| **Estado** | ✅ Mock implementado; tools reales en Fase 3 |

### 3. Dependencia exclusiva del backend real (sin modo mock)

| | |
|---|---|
| **Riesgo IA** | Dashboard y servicios acoplados al backend real desde el inicio, bloqueando desarrollo paralelo. |
| **Corrección** | Todos los servicios de Infrastructure son mocks registrados en `DependencyInjection.cs` con etiqueta `[MOCK]`. El dashboard (Fase 4) tendrá modo mock vía variable de entorno. |
| **Estado** | ✅ Backend con mocks; frontend mock planificado Fase 4 |

### 4. Sobredimensionamiento arquitectónico

| | |
|---|---|
| **Riesgo IA** | Plan inicial de 21 días con múltiples APIs, Redis, Testcontainers, Druid como servicio separado. |
| **Corrección** | MVP acotado a 8–12 h: una API, un Worker, mocks internos, sin Redis ni Testcontainers. Druid vía `MockAnalyticsQueryService` dentro de Infrastructure. |
| **Estado** | ✅ Alineado con `` |

### 5. Validación y contratos HTTP

| | |
|---|---|
| **Riesgo IA** | Validación dispersa o ausente; respuestas HTTP inconsistentes. |
| **Corrección** | `TelemetryEventValidator` centralizado. Ingesta exitosa → `202 Accepted`. Error de validación → `400 Bad Request`. `DateTimeOffset` en todas las entidades y DTOs. |
| **Estado** | ✅ Implementado |

---

*No avanzar a Fase 2 sin confirmación explícita.*
