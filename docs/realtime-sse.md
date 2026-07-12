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
- Publica **una vez** `vehicle-update` con `status=offline` por `(VehicleId, LastEventId)`.
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
- Cada réplica API usa un consumer group distinto: `{RealtimeConsumerGroupBase}-{InstanceId}`.
- Todas las réplicas reciben **todos** los mensajes futuros (no compiten por partición dentro del mismo grupo).

### ID SSE canónico

- En KafkaPush, `id:` del SSE = **offset Kafka** (`ConsumeResult.Offset.Value`).
- `connected`, `heartbeat` y `stream-reset` son efímeros: **sin** `id:` y fuera del replay.

### Replay local acotado

- `FleetSseBroker.SubscribeFrom(lastEventId)` entrega replay + live de forma atómica (`cutoverId`).
- Si `Last-Event-ID` queda fuera del buffer → `event: stream-reset` con `reason`:
  - `replay-gap`
  - `instance-restarted`
  - `invalid-last-event-id`
- El cliente web limpia el cursor, recarga snapshot completo y continúa con eventos live.

### Configuración (`Sse` / `Kafka`)

| Opción | Default | Descripción |
|--------|---------|-------------|
| `Kafka:RealtimeConsumerGroupBase` | `fleet-realtime-sse` | Prefijo del consumer group |
| `Sse:InstanceId` | `HOSTNAME` / `api-local` | Sufijo estable por proceso |
| `Sse:RequireSingleRealtimePartition` | `true` | Falla al iniciar si el tópico tiene >1 partición |
| `Sse:ReplayBufferSize` | `200` | Eventos retenidos para replay local |

### Cliente web

- `SseParser` soporta `id:`, `event:`, `data:`, `retry:`.
- `useSseStream` envía `Last-Event-ID` por header en reconexión y persiste el cursor en `sessionStorage`.

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
