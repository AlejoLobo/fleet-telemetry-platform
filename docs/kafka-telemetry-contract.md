# Contrato Kafka de telemetría (FT-002)

## Versión soportada

| Campo | Valor |
|-------|-------|
| `schemaVersion` | **1** (única versión soportada en este release) |
| `eventType` | `fleet.telemetry.received` |

Una nueva versión (`schemaVersion = 2`, etc.) **requiere** un DTO y mapper explícitos. No se infiere compatibilidad hacia adelante.

## Campos obligatorios del envelope V1

Un mensaje envelope **debe** incluir explícitamente estos cinco campos en la raíz del JSON:

| Campo | Requisito |
|-------|-----------|
| `schemaVersion` | Entero presente; solo `1` es soportado |
| `eventType` | String no vacío; debe ser `fleet.telemetry.received` |
| `eventId` | GUID presente y distinto de `00000000-0000-0000-0000-000000000000` |
| `occurredAt` | Fecha/hora ISO 8601 presente y distinta del valor por defecto |
| `payload` | Objeto JSON presente; `payload: null` es error distinto (`null_payload`) |

No se aceptan valores implícitos por defecto de C#: un campo ausente o vacío se clasifica como `invalid_envelope` (excepto `payload: null` → `null_payload`).

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

### Integridad entre metadatos y payload

- **Dominio (`TelemetryEvent`)**: `payload.eventId` y `payload.timestamp` son la fuente autoritativa al reconstruir la entidad vía `TelemetryEvent.TryCreate`.
- **`eventId`**: debe ser **exactamente igual** a `payload.eventId`.
- **`occurredAt`**: debe representar el **mismo instante UTC** que `payload.timestamp`, comparando el instante y no solo la representación del offset.

Ejemplos equivalentes (aceptados):

- `occurredAt`: `2026-07-12T08:30:00Z`
- `payload.timestamp`: `2026-07-12T03:30:00-05:00`

Una contradicción entre `eventId`/`occurredAt` del envelope y el payload genera `invalid_envelope`. No se descarta silenciosamente `occurredAt` en favor de `payload.timestamp`.

## Inspección previa de `schemaVersion`

En modo envelope, la deserialización sigue este orden:

1. Parsear el JSON como `JsonDocument`.
2. Verificar que la raíz sea un objeto.
3. Leer `schemaVersion` del JSON crudo.
4. Validar que exista y sea un entero.
5. Si `schemaVersion != 1`, devolver inmediatamente `unsupported_schema_version` **sin** materializar `TelemetryEventEnvelopeV1`.
6. Validar metadatos obligatorios (`eventId`, `occurredAt`, `eventType`, `payload`).
7. Solo entonces deserializar `TelemetryEventEnvelopeV1` y mapear al dominio.

Implicaciones:

- `schemaVersion` ausente o no entero → `invalid_envelope`.
- `schemaVersion = 2` (u otra versión futura), incluso con payload incompatible con V1 → `unsupported_schema_version`, no `invalid_envelope`.
- No se deserializa el payload V1 antes de confirmar que la versión es V1.

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

### Campos reservados del envelope en modo legacy

Con `UseEventEnvelope = false`, la presencia de estos campos de estructura envelope en la raíz del JSON se rechaza como `invalid_envelope`:

- `schemaVersion`
- `eventType`
- `occurredAt`
- `payload`

El contrato legacy **sí** incluye `eventId` como campo plano válido. No se hace fallback ni se ignoran silenciosamente los campos reservados anteriores.

Campos adicionales no reservados (por ejemplo `correlationId`) siguen siendo tolerados.

## Detección de formato (sin fallback ambiguo)

| Modo | Regla |
|------|-------|
| `UseEventEnvelope = true` | El JSON **debe** ser un envelope V1 completo. Mensajes planos legacy → DLQ `invalid_envelope`. |
| `UseEventEnvelope = false` | El JSON **no debe** contener campos reservados del envelope. Envelopes versionados → DLQ `invalid_envelope`. |

No existe `try envelope → catch → legacy`.

## Versiones no soportadas

`schemaVersion` distinto de `1` → error terminal `unsupported_schema_version`:

- No se persiste telemetría.
- Se publica en `telemetry.dead-letter`.
- El offset se confirma **solo** tras DLQ exitosa.
- No se reintenta como fallo transitorio.
- No se intenta procesar como V1.

## Clasificación de errores (DLQ)

| Código | Descripción |
|--------|-------------|
| `invalid_json` | JSON sintácticamente inválido |
| `null_payload` | Payload nulo, vacío o literal `null` |
| `invalid_envelope` | Envelope incompleto, contradictorio o incompatible con el modo configurado |
| `unsupported_schema_version` | Versión de esquema no implementada (inspeccionada antes de deserializar V1) |
| `unknown_event_type` | `eventType` presente pero distinto de `fleet.telemetry.received` |
| `invalid_domain` | DTO válido pero viola invariantes de dominio |

## Serialización

- `System.Text.Json` con camelCase e ISO 8601 UTC.
- DTOs: `TelemetryEventEnvelopeV1`, `TelemetryEventPayloadV1`.
- **No** se serializa `TelemetryEvent` (dominio) como contrato Kafka.

## Idempotencia

Duplicados por `EventId` siguen manejados por la lógica existente del Worker/TimescaleDB; el contrato V1 no altera esa garantía.
