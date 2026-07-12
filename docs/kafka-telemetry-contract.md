# Contrato Kafka de telemetría (FT-002)

## Versión soportada

| Campo | Valor |
|-------|-------|
| `schemaVersion` | **1** (única versión soportada en este release) |
| `eventType` | `fleet.telemetry.received` |

Una nueva versión (`schemaVersion = 2`, etc.) **requiere** un DTO y mapper explícitos. No se infiere compatibilidad hacia adelante.

## Formato envelope V1

Activar con `Kafka:UseEventEnvelope = true`.

```json
{
  "schemaVersion": 1,
  "eventType": "fleet.telemetry.received",
  "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "occurredAt": "2026-07-12T08:30:00Z",
  "payload": {
    "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "vehicleId": "VH-001",
    "driverId": "DRV-001",
    "timestamp": "2026-07-12T08:30:00Z",
    "latitude": 4.6533,
    "longitude": -74.0836,
    "speedKmh": 88.5,
    "fuelLevelPercent": 42.0,
    "batteryPercent": 91.0,
    "locationSource": "gps"
  }
}
```

### Fuente autoritativa

- **Dominio (`TelemetryEvent`)**: `payload.eventId` y `payload.timestamp` son la fuente autoritativa al reconstruir la entidad vía `TelemetryEvent.TryCreate`.
- **Metadatos de envelope**: `eventId` y `occurredAt` en el envelope deben coincidir con el payload cuando están presentes; se usan para trazabilidad, no para sobrescribir el dominio.

## Formato legacy

Con `Kafka:UseEventEnvelope = false` (default), el mensaje es JSON plano:

```json
{
  "eventId": "...",
  "vehicleId": "VH-001",
  "driverId": "DRV-001",
  "timestamp": "2026-07-12T08:30:00Z",
  "latitude": 4.6533,
  "longitude": -74.0836,
  "speedKmh": 88.5,
  "fuelLevelPercent": 42.0,
  "batteryPercent": 91.0,
  "locationSource": "gps"
}
```

## Detección de formato (sin fallback ambiguo)

| Modo | Regla |
|------|-------|
| `UseEventEnvelope = true` | El JSON **debe** tener `schemaVersion` **y** `payload` en la raíz. Mensajes planos legacy → DLQ `invalid_envelope`. |
| `UseEventEnvelope = false` | El JSON **no debe** tener estructura de envelope. Envelopes versionados → DLQ `invalid_envelope`. |

No existe `try envelope → catch → legacy`.

## Versiones no soportadas

`schemaVersion` distinto de `1` → error terminal `unsupported_schema_version`:

- No se persiste telemetría.
- Se publica en `telemetry.dead-letter`.
- El offset se confirma **solo** tras DLQ exitosa.
- No se reintenta como fallo transitorio.

## Clasificación de errores (DLQ)

| Código | Descripción |
|--------|-------------|
| `invalid_json` | JSON sintácticamente inválido |
| `null_payload` | Payload nulo, vacío o literal `null` |
| `invalid_envelope` | Envelope incompleto o incompatible con el modo configurado |
| `null_payload` | `payload: null` en envelope |
| `unsupported_schema_version` | Versión de esquema no implementada |
| `unknown_event_type` | `eventType` distinto de `fleet.telemetry.received` |
| `invalid_domain` | DTO válido pero viola invariantes de dominio |

## Serialización

- `System.Text.Json` con camelCase e ISO 8601 UTC.
- DTOs: `TelemetryEventEnvelopeV1`, `TelemetryEventPayloadV1`.
- **No** se serializa `TelemetryEvent` (dominio) como contrato Kafka.

## Idempotencia

Duplicados por `EventId` siguen manejados por la lógica existente del Worker/TimescaleDB; el contrato V1 no altera esa garantía.
