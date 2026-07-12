# API y operaciones

## Endpoints

| Método | Ruta | Auth* | Descripción |
|--------|------|-------|-------------|
| `GET` | `/health` | No | Estado + circuit breakers |
| `GET` | `/health/circuit-breakers` | No | Detalle de circuitos |
| `GET` | `/health/live` | No | Liveness (sin dependencias) |
| `GET` | `/health/ready` | No | Readiness: TimescaleDB + Kafka metadata |
| `GET` | `/api/ops/summary` | Si Auth on | Resumen operativo |
| `POST` | `/api/telemetry` | Si Auth on | Ingesta un evento → Kafka (`202`) |
| `POST` | `/api/telemetry/batch` | Si Auth on | Lote (sync mobile) |
| `GET` | `/api/telemetry/{vehicleId}` | No | Historial paginado (`?from=&to=&pageSize=&cursor=`) |
| `GET` | `/api/fleet` | No | Flota paginada (`?pageSize=&cursor=&liveOnly=&excludeSimulated=`) |
| `GET` | `/api/fleet/{vehicleId}` | No | Estado de un vehículo |
| `GET` | `/api/alerts` | No | Alertas abiertas |
| `PATCH` | `/api/alerts/{id}/acknowledge` | Si Auth on | Confirmar alerta |
| `GET` | `/api/events/stream` | No | SSE |
| `POST` | `/api/auth/login` | — | JWT (si Auth habilitado) |
| `GET` | `/api/auth/status` | No | `{ enabled }` |
| `POST` | `/api/ai/query` | No | Agente IA |

\* `AuthorizeWhenEnabled`: exige JWT solo si `Auth:Enabled=true`.

OpenAPI (Development): `http://localhost:5000/openapi/v1.json`

## Observabilidad (MVP)

Sin Prometheus/Grafana/OpenTelemetry en esta fase.

### `GET /health/live`

```json
{ "status": "alive", "service": "fleet-telemetry-api", "timestamp": "..." }
```

### `GET /health/ready`

Comprueba `CanConnect` a TimescaleDB y metadata Kafka (sin publicar). `503` si alguna falla. Respuesta incluye `checks` (`ok` / `unavailable`) **sin** connection strings ni secretos.

### `GET /api/ops/summary`

Campos: `totalVehicles`, `activeVehicles`, `criticalAlerts`, `lastTelemetryAt`, `sseMode` (`polling` | `kafka-push`), `telemetryTopic`, `deadLetterTopic`.

Implementación: agregados SQL sobre `fleet_vehicle_state` y `COUNT` de alertas críticas abiertas (`IFleetStateAggregateRepository`).

```bash
curl http://localhost:5000/health/live
curl http://localhost:5000/health/ready
curl http://localhost:5000/api/ops/summary
```

## Auth

- Default: `Auth__Enabled=false` (demo abierta).
- Con Auth: `JwtSecret` ≥ 32 caracteres y `DemoPassword` no vacío (`ConfigurationValidator`).
- Login: `POST /api/auth/login` con usuario/password demo.

## Ejemplos

### Ingesta

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
    \"speedKmh\": 130.0,
    \"fuelLevelPercent\": 10.0,
    \"batteryPercent\": 95.0
  }"
```

Respuesta: `202 Accepted`. La API valida el DTO (`TelemetryEventValidator`) y publica en `telemetry.raw`.

### Consulta

```bash
curl "http://localhost:5000/api/fleet?pageSize=100"
curl "http://localhost:5000/api/fleet?pageSize=100&cursor=<opaco>"
curl http://localhost:5000/api/alerts
curl "http://localhost:5000/api/telemetry/VH-001?from=2026-07-08T00:00:00Z&pageSize=200"
```

#### Paginación por cursor

- `GET /api/fleet` devuelve `CursorPage<VehicleLatestStatusResponse>`: `{ items, nextCursor, hasMore }`.
- Orden estable: `VehicleId` ascendente. `pageSize` default 100, máximo 500.
- `liveOnly`: filtra en SQL `LastTimestamp >= now - OnlineThresholdMinutes`.
- `excludeSimulated`: excluye vehículos cuyo **estado más reciente** es `simulated` (no retrocede a GPS antiguo).
- El cursor es opaco (Base64URL + JSON validado). Reutilizarlo con filtros distintos → `400`.

- `GET /api/telemetry/{vehicleId}` devuelve `CursorPage<TelemetryEventResponse>`.
- Orden: `Timestamp DESC`, `EventId DESC` (keyset, sin OFFSET).
- En la primera página sin `to`, el servidor fija un límite superior estable incluido en el cursor.
- Rango máximo configurable (`QueryLimits:HistoryMaxRangeDays`, default 7 días).

### Agente IA

```bash
curl -X POST http://localhost:5000/api/ai/query \
  -H "Content-Type: application/json" \
  -d "{\"question\": \"¿Qué vehículos tienen alertas críticas?\"}"
```

Tools internas + pulido OpenAI opcional si hay `OpenAI__ApiKey`.

## Resiliencia

| Dependencia | Política |
|-------------|----------|
| Kafka produce | Circuit breaker + retry |
| TimescaleDB (Worker) | Circuit breaker + retry; sin commit si abierto |
| OpenAI | Timeout 20s + circuit breaker; fallback a respuesta operativa |

Si Kafka está abierto, ingesta → `503` + `Retry-After`.
