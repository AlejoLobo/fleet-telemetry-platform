# Arquitectura

## Clean Architecture

| Proyecto | Responsabilidad |
|----------|-----------------|
| `FleetTelemetry.Domain` | Entidades (`TelemetryEvent`, `FleetAlert`) |
| `FleetTelemetry.Application` | Casos de uso, DTOs, validadores, interfaces |
| `FleetTelemetry.Infrastructure` | Kafka, TimescaleDB, resiliencia, SSE broker, OpenAI |
| `FleetTelemetry.Api` | HTTP: ingesta, consultas, health, ops, SSE, IA |
| `FleetTelemetry.Worker` | Consumidor Kafka + `TelemetryMessageProcessor` |

**Una sola API** y **un solo Worker**. No hay segundo servicio de ingesta.

## Flujo de telemetría

```mermaid
sequenceDiagram
  participant Client
  participant Api
  participant Kafka
  participant Worker
  participant Db as TimescaleDB

  Client->>Api: POST /api/telemetry
  Api->>Api: TelemetryEventValidator
  Api->>Kafka: produce telemetry.raw
  Api-->>Client: 202 Accepted
  Worker->>Kafka: consume
  Worker->>Worker: Deserialize + DomainValidator
  alt payload inválido
    Worker->>Kafka: produce telemetry.dead-letter
    Worker->>Kafka: commit offset
  else válido
    Worker->>Db: processed_events + telemetry_events + alerts + fleet_vehicle_state
    Worker->>Kafka: commit offset
  end
  Client->>Api: GET /api/fleet?pageSize=100
  Api->>Db: fleet_vehicle_state (cursor, filtros SQL)
  Api-->>Client: CursorPage JSON
```

## Decisiones clave

- **Ingesta desacoplada:** el controller/use case de API **no** persiste; solo publica en Kafka.
- **DI por perfil:** `InfrastructureProfile.Api` vs `Worker` en `DependencyInjection.cs`.
- **Idempotencia:** `processed_events` con `ON CONFLICT DO NOTHING` en la misma transacción que telemetría y alertas.
- **Validación en dos capas:**
  - API: `TelemetryEventValidator` (DTO `TelemetryEventRequest`).
  - Worker: `TelemetryDomainEventValidator` (entidad tras deserializar JSON de Kafka).
- **SSE:** modo **KafkaPush** por defecto (`fleet.realtime` → API → SSE). Fan-out multi-réplica con consumer group por instancia; offset Kafka como `id` SSE; replay local acotado y `stream-reset` ante gaps. Modo alternativo: polling a TimescaleDB. Ver [realtime-sse.md](realtime-sse.md).
- **Expiración de conectividad:** `FleetConnectivityExpiryHostedService` en el Worker publica `offline` sin telemetría nueva.
- **Resiliencia:** circuit breaker + retry en Kafka produce, DB (solo transitorios vía `DatabaseTransientFailureClassifier`) y OpenAI. Estado en `GET /health`.
- **Read model de flota:** `fleet_vehicle_state` (1 fila/vehículo), actualizado en la misma transacción del Worker con UPSERT protegido ante eventos fuera de orden. Consultas `GET /api/fleet` paginadas por cursor sobre esta tabla (no `DISTINCT ON` global).
- **Historial paginado:** `GET /api/telemetry/{vehicleId}` usa keyset (`Timestamp DESC`, `EventId DESC`) con límite superior estable en el cursor.

## Alertas (Worker)

| Tipo | Condición | Severidad |
|------|-----------|-----------|
| `overspeed` | `speedKmh` > 120 | critical |
| `low_fuel` | `fuelLevelPercent` < 15 | warning |
| `low_battery` | `batteryPercent` < 20 | warning |

## Tópicos Kafka

| Tópico | Uso |
|--------|-----|
| `telemetry.raw` | Ingesta (legacy plano o envelope V1 según `Kafka:UseEventEnvelope`) |
| `telemetry.dead-letter` | DLQ |

Contrato versionado V1: [kafka-telemetry-contract.md](kafka-telemetry-contract.md).

Detalle de procesamiento y DLQ: [worker-and-dlq.md](worker-and-dlq.md).
