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

## Taxonomía de errores (procesamiento)

Clasificación deliberadamente simple; los errores desconocidos **no** se tratan como fallos del mensaje:

| Clase | Comportamiento |
|-------|----------------|
| Datos / contrato inválido (`TelemetryKafkaContractException`, `ArgumentException` en validación, payload vacío) | Preparar DLQ → commit **solo** tras publicación DLQ exitosa |
| Transitorio reconocido (`BrokenCircuitException`, `DatabaseTransientFailureClassifier.IsTransient`) | Retry del mismo offset según `MaxProcessingAttempts`; al agotar → DLQ `processing_failure` |
| Excepción inesperada (p. ej. `NullReferenceException`, `InvalidOperationException` de programación) | `LogCritical` (tipo, topic, partition, offset) → `StopApplication()` → **sin** DLQ ni commit |
| Fallo al publicar DLQ | Reintento solo de publicación; al límite → `LogCritical` + stop **sin** commit (comportamiento actual) |
| Cancelación solicitada | Sin commit |

Los errores desconocidos no se clasifican automáticamente como problemas del mensaje: ocultarlos en DLQ provocaría commits incorrectos y pérdida de visibilidad sobre defectos sistémicos.

## At-least-once (mismo offset)

Tras `Consume`, no se llama a `Consume()` de nuevo hasta:

1. Procesamiento OK o duplicado → commit.
2. Payload / contrato inválido → DLQ → commit (si DLQ OK).
3. Fallo transitorio reconocido → reintento + backoff; al agotar intentos → DLQ `processing_failure`.
4. Excepción inesperada → stop del Worker **sin** DLQ ni commit (redelivery tras corrección/reinicio).
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

Polly y el processor usan `DatabaseTransientFailureClassifier.IsTransient` para reintentos. Un fallo de base de datos **no** marcado como transitorio se trata como excepción inesperada (stop sin DLQ/commit), no como “permanente del mensaje”.

## Verificar DLQ localmente

```bash
printf '%s\n' '{"vehicleId":"SMOKE-DLQ"}' | \
  docker exec -i fleet-redpanda rpk topic produce telemetry.raw --brokers localhost:9092

docker exec fleet-redpanda rpk topic consume telemetry.dead-letter -n 5 --brokers localhost:9092
```

O smoke: [../scripts/smoke-test.ps1](../scripts/smoke-test.ps1).

## Tests

`FleetTelemetry.Worker.Tests` (unitarios) + `FleetTelemetry.Integration.Tests` (Kafka/Timescale reales, DLQ productiva y doubles). Ver [testing.md](testing.md).
