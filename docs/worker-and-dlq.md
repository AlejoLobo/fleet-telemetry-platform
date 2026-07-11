# Worker Kafka y Dead Letter Queue

## Garantías

- Kafka: **at-least-once** (no exactly-once end-to-end).
- Idempotencia por `EventId` en TimescaleDB.
- Commit manual (`EnableAutoCommit = false`).
- Reintento del **mismo** `ConsumeResult` antes del siguiente `Consume()`.
- DLQ exitosa antes del commit; fallo de DLQ ⇒ **sin** commit (redelivery tras reinicio).
- Orden **solo** dentro de una partición; no hay orden global.
- El Worker procesa **serialmente**; un mensaje bloqueado puede detener todas las particiones asignadas a esa instancia. Evolución productiva: pause/resume por partición.

## Componentes

| Pieza | Rol |
|-------|-----|
| `TelemetryConsumerWorker` | `Consume` → reintentos del mismo offset → `Commit` solo al terminal |
| `TelemetryMessageCoordinator` | Separa procesamiento y publicación DLQ (reintento DLQ sin reprocesar) |
| `TelemetryMessageProcessor` | Deserializar, validar, clasificar fallos, preparar DLQ (stateless) |
| `KafkaProcessingRetryBackoff` | Backoff exponencial + jitter **acotado** al máximo |
| `DeadLetterPublishRetrySession` | Límite de fallos al publicar DLQ; detiene el host sin commit |
| `DatabaseTransientFailureClassifier` | SQLSTATE / Npgsql → transitorio vs permanente |
| `IDeadLetterPublisher` | Publica en `telemetry.dead-letter` (`KafkaDeadLetterPublisher` en producción) |

## Tests de integración

- **Publisher productivo:** `UseProductionDeadLetterPublisher = true` en `TelemetryConsumerWorkerTestHost` conserva `KafkaDeadLetterPublisher` registrado por infraestructura. Las pruebas consumen el tópico DLQ real y verifican payload camelCase, metadatos y clave `topic:partition:offset` antes del commit.
- **Double controlable:** `ControllableDeadLetterPublisher` reemplaza `IDeadLetterPublisher` solo cuando se necesita simular fallos de publicación, redelivery tras agotar intentos o contar reintentos sin depender del broker.
- **Esquema TimescaleDB:** el Worker inicializa el esquema al arrancar; el test host ya no llama `DatabaseInitializer` en `CreateAsync`. La concurrencia del DDL se protege con `pg_advisory_lock` en una única conexión abierta.

## At-least-once (mismo offset)

Tras `Consume`, no se llama a `Consume()` de nuevo hasta:

1. Procesamiento OK o duplicado → commit.
2. Payload inválido/vacío/whitespace → DLQ `invalid_payload` → commit (si DLQ OK).
3. Fallo transitorio → reintento + backoff; al agotar intentos → DLQ `processing_failure`.
4. Fallo permanente → DLQ inmediata.
5. Fallo al publicar DLQ → reintento **solo** de publicación (sin reprocesar ni reiniciar intentos de negocio); al límite → `LogCritical` + stop del Worker **sin** commit.
6. Cancelación / apagado → sin commit.

## Resultados de procesamiento

| Resultado | Commit | Uso |
|-----------|--------|-----|
| `ProcessedAndCommit` | Sí | Evento válido / duplicado tratado |
| `RequiresDeadLetterPublish` | Sí (tras publicar DLQ) | Procesamiento terminal; el coordinador publica DLQ y luego confirma offset |
| `RetryWithoutCommit` | No | Reintentar el mismo offset tras backoff |

`IgnoreWithoutCommit` fue **eliminado**: no hay camino que avance sin DLQ ni commit explícito para payloads vacíos.

## Configuración (`Kafka`)

| Clave | Default | Rol |
|-------|---------|-----|
| `MaxProcessingAttempts` | `3` | Intentos del mismo mensaje antes de DLQ (transitorios) |
| `RetryInitialDelayMilliseconds` | `500` | Delay base |
| `RetryMaxDelayMilliseconds` | `5000` | Tope del backoff (incluye jitter) |
| `MaxDeadLetterPublishAttempts` | `5` | Fallos de publicación DLQ antes de detener el Worker |
| `MaxPollIntervalMilliseconds` | `300000` | `MaxPollIntervalMs` del consumidor; debe superar el peor escenario de procesamiento + Polly + backoff + DLQ |

## Validación de payload

`string.IsNullOrWhiteSpace` → DLQ `invalid_payload`. También JSON inválido, `null` literal y dominio inválido.

## Fallos DB

Polly y el processor usan `DatabaseTransientFailureClassifier.IsTransient`. Errores permanentes (constraints, esquema, tipos) van a DLQ inmediata; no abren el circuit breaker ni agotan reintentos inútiles.

## Verificar DLQ localmente

```bash
printf '%s\n' '{"vehicleId":"SMOKE-DLQ"}' | \
  docker exec -i fleet-redpanda rpk topic produce telemetry.raw --brokers localhost:9092

docker exec fleet-redpanda rpk topic consume telemetry.dead-letter -n 5 --brokers localhost:9092
```

O smoke: [../scripts/smoke-test.ps1](../scripts/smoke-test.ps1).

## Tests

`FleetTelemetry.Worker.Tests` (unitarios) + `FleetTelemetry.Integration.Tests` (Kafka/Timescale reales, DLQ productiva y doubles). Ver [testing.md](testing.md).
