# Tiempo real SSE

Índice: [README.md](README.md) · Arquitectura: [architecture.md](architecture.md).

## Modo actual: KafkaPush

El stack local y la configuración por defecto usan `Sse:Mode = KafkaPush`:

1. El **Worker** persiste telemetría y publica en Kafka (`fleet.realtime`) eventos `vehicle-update` (canónico) y `alert`.
2. La **API** consume `fleet.realtime` con `FleetSseKafkaPushHostedService` y reenvía al `FleetSseBroker`.
3. El dashboard abre `GET /api/events/stream` y procesa SSE con `useSseStream`.

### Contrato de eventos

| Evento | Estado | Descripción |
|--------|--------|-------------|
| `vehicle-update` | **Canónico** | Un vehículo por mensaje; payload `VehicleLatestStatusResponse` (incluye `lastEventId`) |
| `fleet-update` | Legacy | Array de vehículos; soportado por compatibilidad en el cliente |
| `alert` | Activo | Nueva alerta operativa |
| `heartbeat` | Activo | Mantiene viva la conexión |
| `stream-reset` | Activo | Cortocircuito de cursor; el cliente ejecuta snapshot REST |

El cliente web fusiona parches `vehicle-update` con el snapshot REST usando `lastSeenAt` + `lastEventId` (misma regla que PostgreSQL).

## Modo alternativo: Polling

Con `Sse:Mode = Polling`, `FleetSsePollerHostedService` consulta TimescaleDB periódicamente y emite `fleet-update` cuando el hash del snapshot cambia.

Intervalos (`Sse` en `appsettings.json`):

| Modo | Default | Cuándo |
|------|---------|--------|
| Activo | 3 s | La flota cambió en el ciclo anterior |
| Idle | 10 s | Sin cambios |

## Conectividad online / offline

La conectividad se calcula con `VehicleConnectivityStatus.Resolve`:

```
LastTimestamp >= now - OnlineThresholdMinutes  →  online
en caso contrario                              →  offline
```

`OnlineThresholdMinutes` está en `QueryLimits` (default 5).

### Transición a offline sin telemetría nueva (KafkaPush)

En KafkaPush, un vehículo que deja de transmitir no genera `vehicle-update` por sí solo. El Worker ejecuta `FleetConnectivityExpiryHostedService`:

- Consulta incremental sobre `fleet_vehicle_state.LastTimestamp` (índice `ix_fleet_vehicle_state_last_timestamp`).
- Detecta vehículos que acaban de cruzar el umbral.
- Publica **una vez** `vehicle-update` con `status=offline` por `(DeviceId, LastEventId)`.
- El payload offline conserva `vehicleName` y `vehicleType` del registro del dispositivo (misma estructura que online).
- No recorre toda la flota en cada ciclo; usa ventana deslizante entre umbrales consecutivos.

Configuración (`Sse`):

| Opción | Default | Descripción |
|--------|---------|-------------|
| `ConnectivityExpiryIntervalSeconds` | 30 | Intervalo del ciclo |
| `ConnectivityExpiryLookbackSeconds` | 90 | Ventana inicial si no hay umbral previo |
| `ConnectivityExpiryBatchSize` | 200 | Tope por ciclo |

Cuando llega telemetría nueva, el Worker publica `online` y limpia el marcador de offline publicado.

## Multi-réplica y replay (FT-005)

### Fan-out entre réplicas API

- El tópico `fleet.realtime` debe tener **exactamente 1 partición** (orden global de offsets).
- Cada réplica hace **Assign manual** de la partición 0 (sin `Subscribe` ni rebalance de consumer group).
- El `GroupId` sigue siendo único por réplica (`{RealtimeConsumerGroupBase}-{InstanceId}`) solo como identidad; **no** coordina offsets entre réplicas.
- Todas las réplicas reciben **todos** los mensajes futuros desde su Assign.
- El snapshot REST cubre el estado anterior al arranque de la réplica.

### ID SSE canónico

- En KafkaPush, `id:` del SSE = **offset Kafka** (`ConsumeResult.Offset.Value`).
- `connected`, `heartbeat` y `stream-reset` son efímeros: **sin** `id:` y fuera del replay.

### Snapshot inicial sin `Last-Event-ID`

Al arrancar, la réplica consulta watermarks, fija `baseline = High - 1`, inicializa el broker y hace Assign en High.

Una suscripción `Missing` (sin `Last-Event-ID`):

- emite `stream-reset` con `reason=initial-snapshot` y `latestEventId = baseline`;
- el cliente bloquea live hasta completar `refreshForResync`;
- los live con `id > baseline` se aplican después, una vez y en orden.

### Replay local acotado

- La admisión SSE en KafkaPush pasa por `RealtimeStreamCoordinator.TryOpenStream` (Ready + `SubscribeFrom` atómicos bajo el mismo lock).
- Si `Last-Event-ID` queda fuera del buffer → `event: stream-reset` con `reason`:
  - `initial-snapshot` — primera conexión / sin cursor
  - `replay-gap`
  - `instance-restarted`
  - `invalid-last-event-id`
  - `invalid-payload-gap` — offsets Kafka inválidos (tombstone/contrato) en el rango
- El cliente web limpia el cursor, recarga snapshot completo y continúa con eventos live.

### Estado del stream (`RealtimeStreamCoordinator`)

Estados: `Starting` → `Ready` ↔ `Recovering` → (`Faulted`).

| Estado | SSE `/api/events/stream` | `/health/ready` (`kafka_push`) |
|--------|--------------------------|--------------------------------|
| Starting | **503** | `starting` |
| Recovering | **503** | `recovering` |
| Ready | admite stream | `ok` |
| Faulted | **503** | `faulted` |
| Polling (sin KafkaPush) | admite | `bypassed` |

`EnterRecovering` / `EnterFaulted` incrementan epoch, cierran todas las suscripciones (`CompleteAllSubscribers`) e impiden nuevas admisiones.

### Loop con pendingRecord

- Un solo registro pendiente: no se Consume N+1 hasta completar el pendiente.
- Accepted / Duplicate / InvalidPermanent → avanzan watermark del broker y liberan el pending.
- TransientPublishFailure → conserva el mismo `ConsumeResult`, `EnterRecovering` si estaba Ready, backoff, reintenta **sin Seek**.
- Idle o Completed en `Recovering`/`Starting` → `EnterReady` (tópico quieto recupera admisión SSE).
- FatalTransportFailure → `EnterRecovering`, disponer consumidor, crear uno nuevo vía `IRealtimeKafkaConsumerFactory`, resolver Assign (Low/High), poll saludable, `EnterReady`.
- No hay commits Kafka para coordinar la réplica.

### Recuperación

- `resumeOffset = LastProcessedExternalOffset + 1`
- Si `Low <= resumeOffset <= High` → Assign en `resumeOffset`.
- Si `resumeOffset < Low` o `resumeOffset > High` → `ResetToBaseline(High - 1)`, Assign en High, `initial-snapshot`.
- Ready solo tras Idle/Completed saludable con el consumidor nuevo.
- Fallos transitorios en validación de metadata, creación del consumidor, watermarks, Assign o materialización de Assign se recuperan disponiendo la sesión, backoff cancelable (200 ms → 5 s) y recreando la sesión; no pasan a `Faulted` ni habilitan Ready sin poll saludable.
- El backoff de sesión (`consecutiveFailures`) solo se reinicia tras el primer `Idle`/`Completed` saludable; un `PrepareAssignment` exitoso no reinicia la racha.
- Metadata Kafka inaccesible o tópico aún no disponible → `RealtimeTopicMetadataUnavailableException` (transitoria, Starting). `Partitions.Count != 1` → `RealtimeTopicPartitionCountException` (permanente, Faulted). No hay Assign ni Ready hasta validar una partición.
- Si la materialización de Assign expira sin confirmar la posición objetivo, se lanza un error transitorio específico y se recrea el consumidor.

### Configuración (`Sse` / `Kafka`)

| Opción | Default | Descripción |
|--------|---------|-------------|
| `Kafka:RealtimeConsumerGroupBase` | `fleet-realtime-sse` | Prefijo de identidad del GroupId |
| `Sse:InstanceId` | `HOSTNAME` o `MachineName` | Sufijo estable por proceso (post-configure) |
| `Sse:RequireSingleRealtimePartition` | `true` | Exige 1 partición; metadata transitoria reintenta en Starting; conteo ≠ 1 → Faulted |
| `Sse:ReplayBufferSize` | `200` | Eventos retenidos para replay local |

### Cliente web

- `SseParser` soporta `id:`, `event:`, `data:`, `retry:`.
- `useSseStream` envía `Last-Event-ID` por header en reconexión y persiste el cursor en `sessionStorage`.
- Tras `stream-reset` (incluido `initial-snapshot`) no escribe cursor si la conexión ya fue reemplazada o el token cambió.

## Autenticación segura para SSE

`EventSource` nativo **no permite** cabeceras `Authorization`. No usar JWT en query string.

### Enfoque recomendado (producción)

1. Ticket de corta duración (`POST /api/auth/sse-ticket`) + `fetch` stream con cabecera.
2. O `fetch('/api/events/stream', { headers: { Authorization: '...' } })` con JWT de vida corta.

### Estado MVP

- `Auth:Enabled=false` (default): SSE abierto para demo local.
- `Auth:Enabled=true`: exige política `FleetRead`; el dashboard usa fetch-stream + cabecera.

## Truncación en el dashboard

El snapshot de flota en web está acotado (`maxVehicles`, default 5000). Cuando `truncated=true`:

- KPIs globales usan `/api/ops/summary` (`aggregationSource: ops`).
- El panel lateral muestra **mostrados vs total** (no `vehicles.length` como total global).

Ver [api-and-ops.md](api-and-ops.md).
