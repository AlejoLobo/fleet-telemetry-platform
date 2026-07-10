# Worker Kafka y Dead Letter Queue

## Componentes

| Pieza | Rol |
|-------|-----|
| `TelemetryConsumerWorker` | Loop: `Consume` → processor → `Commit` según resultado |
| `TelemetryMessageProcessor` | Deserializar, validar dominio, procesar, publicar DLQ |
| `IDeadLetterPublisher` / `KafkaDeadLetterPublisher` | Publica en `telemetry.dead-letter` |
| `ProcessTelemetryEventUseCase` | Persistencia vía UoW transaccional |

`EnableAutoCommit = false`. El offset se confirma **solo** cuando el resultado indica commit.

## Resultados de procesamiento

| Resultado | Commit | Uso |
|-----------|--------|-----|
| `ProcessedAndCommit` | Sí | Evento válido procesado |
| `SentToDeadLetterAndCommit` | Sí | DLQ publicada con éxito |
| `RetryWithoutCommit` | No | Error transitorio o reintento pendiente |
| `IgnoreWithoutCommit` | No | Payload vacío / ignorado |

Si `PublishAsync` de DLQ lanza, el Worker **no** confirma offset.

## Validación

1. `TelemetryEventJsonSerializer.Deserialize` — JSON → `TelemetryEvent` (no aplica reglas de negocio).
2. `TelemetryDomainEventValidator.Validate` — `EventId`, `VehicleId`, `Timestamp`, lat/lon, `SpeedKmh ≥ 0`.

Distinto de `TelemetryEventValidator` (API, DTO de `POST /api/telemetry`).

JSON parcial como `{"vehicleId":"VH-001"}` deserializa pero falla dominio → DLQ `invalid_payload`.

## Comportamiento DLQ

Tópico: `telemetry.dead-letter` (`KafkaOptions.DeadLetterTopic`).

Payload DLQ (camelCase): `originalPayload`, `reason`, `exceptionMessage`, `originalTopic`, `partition`, `offset`, `occurredAt`.

| Caso | Reason | DLQ | Commit |
|------|--------|-----|--------|
| JSON inválido / dominio inválido | `invalid_payload` | Sí | Solo si DLQ OK |
| Error no transitorio ≥ `MaxProcessingAttempts` | `processing_failure` | Sí | Solo si DLQ OK |
| `TimeoutException` / `NpgsqlException` / `DbUpdateException` / circuit breaker | — | No | No (retry + backoff) |

## Verificar DLQ localmente

```bash
# Payload inválido directo a Kafka (la API rechazaría el DTO con 400)
printf '%s\n' '{"vehicleId":"SMOKE-DLQ"}' | \
  docker exec -i fleet-redpanda rpk topic produce telemetry.raw --brokers localhost:9092

# Consumir DLQ
docker exec fleet-redpanda rpk topic consume telemetry.dead-letter -n 5 --brokers localhost:9092
```

O usar el smoke test: [../scripts/smoke-test.ps1](../scripts/smoke-test.ps1).

## Tests unitarios del processor

Proyecto `FleetTelemetry.Worker.Tests` — fakes de `IDeadLetterPublisher`, sin Kafka real. Ver [testing.md](testing.md).
