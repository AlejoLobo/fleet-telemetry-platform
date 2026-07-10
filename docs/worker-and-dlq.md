# Worker Kafka y Dead Letter Queue

## Componentes

| Pieza | Rol |
|-------|-----|
| `TelemetryConsumerWorker` | `Consume` → reintentos del **mismo** offset → `Commit` solo al terminal |
| `TelemetryMessageProcessor` | Deserializar, validar dominio, procesar, publicar DLQ (sin estado en memoria) |
| `KafkaProcessingRetryBackoff` | Backoff exponencial + jitter entre intentos |
| `IDeadLetterPublisher` / `KafkaDeadLetterPublisher` | Publica en `telemetry.dead-letter` |
| `ProcessTelemetryEventUseCase` | Persistencia vía UoW transaccional |

`EnableAutoCommit = false`. El offset se confirma **solo** cuando el resultado indica commit.

## At-least-once (mismo offset)

Tras `Consume`, el Worker **no** llama a `Consume()` de nuevo hasta resolver el mensaje actual:

1. Procesamiento OK o duplicado → commit.
2. Payload inválido → DLQ `invalid_payload` → commit (si DLQ OK).
3. Fallo de procesamiento → reintento del mismo `ConsumeResult` con backoff.
4. `currentAttempt >= MaxProcessingAttempts` → DLQ `processing_failure` → commit (si DLQ OK).
5. Fallo al publicar DLQ → no commit; se reintenta el mismo mensaje.
6. Cancelación / apagado → sale sin commit.

Así no se confirma un offset posterior mientras uno anterior sigue pendiente (garantía at-least-once + idempotencia por `EventId` en DB).

## Resultados de procesamiento

| Resultado | Commit | Uso |
|-----------|--------|-----|
| `ProcessedAndCommit` | Sí | Evento válido procesado |
| `SentToDeadLetterAndCommit` | Sí | DLQ publicada con éxito |
| `RetryWithoutCommit` | No | Reintentar el **mismo** offset tras backoff |
| `IgnoreWithoutCommit` | No | Payload vacío / ignorado |

`ProcessAsync(message, currentAttempt, ...)` recibe el intento desde el consumidor (no hay contador en memoria en el processor).

## Configuración (`Kafka`)

| Clave | Default | Rol |
|-------|---------|-----|
| `MaxProcessingAttempts` | `3` | Intentos del mismo mensaje antes de DLQ |
| `RetryInitialDelayMilliseconds` | `500` | Delay base del backoff |
| `RetryMaxDelayMilliseconds` | `5000` | Tope del backoff |

Validadas al arranque (`ConfigurationValidator`).

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
| Cualquier fallo de procesamiento con `currentAttempt >= MaxProcessingAttempts` | `processing_failure` | Sí | Solo si DLQ OK |
| Fallo con `currentAttempt < Max` (transitorio, circuit breaker u otro) | — | No | No (retry mismo offset + backoff) |

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

Proyecto `FleetTelemetry.Worker.Tests` — fakes de `IDeadLetterPublisher`, backoff unitario, sin Kafka real. Ver [testing.md](testing.md).
