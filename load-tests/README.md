# Pruebas de carga k6 (Fase 6)

Simula ingesta concurrente contra `POST /api/telemetry` con **caos controlado**: cientos de vehículos, duplicados y payloads inválidos.

Cada vehículo (`VH-001` … `VH-N`) se ubica en una **zona distinta de Bogotá** (Chapinero, Suba, Kennedy, etc.) con coordenadas aleatorias dentro de la zona. Aproximadamente **62% online** (timestamp reciente) y **38% offline** (timestamp antiguo).

## Requisitos

- [k6](https://k6.io/docs/get-started/installation/) instalado
- API + Worker + Docker (Kafka, TimescaleDB) en ejecución

## Variables de entorno

| Variable | Default | Descripción |
|----------|---------|-------------|
| `API_URL` | `http://localhost:5000` | URL base de la API |
| `VUS` | `10` | Usuarios virtuales concurrentes |
| `DURATION` | `30s` | Duración del escenario |
| `VEHICLES` | `300` | Rango de vehículos simulados (`VH-001` … `VH-300`) |
| `DUPLICATE_RATE` | `0.1` | Fracción de requests con `eventId` reutilizado (10%) |
| `ERROR_RATE` | `0.05` | Fracción de payloads inválidos intencionales (5%) |
| `AUTH_TOKEN` | *(vacío)* | JWT si `Auth:Enabled=true` |

## Ejecución

```bash
cd load-tests

# Carga básica con defaults (300 vehículos, 10% duplicados, 5% errores)
k6 run telemetry-ingest.js

# Carga personalizada
k6 run \
  -e API_URL=http://localhost:5000 \
  -e VUS=25 \
  -e DURATION=1m \
  -e VEHICLES=500 \
  -e DUPLICATE_RATE=0.1 \
  -e ERROR_RATE=0.05 \
  telemetry-ingest.js

# Con JWT (Auth:Enabled=true)
k6 run -e AUTH_TOKEN=<jwt> telemetry-ingest.js
```

## Caos controlado

### Duplicados (10% por defecto)

El script reutiliza `eventId` de un pool fijo. La API acepta todos con `202`, pero el Worker debe **omitir duplicados** gracias a `processed_events` + idempotencia transaccional.

Métrica k6: `telemetry_duplicate_sent`.

### Errores intencionales (5% por defecto)

Se envían payloads inválidos (vehículo vacío, coordenadas fuera de rango, velocidad negativa). La API debe responder `400`.

Métrica k6: `telemetry_intentional_invalid` y `telemetry_intentional_error_rate`.

Estos errores **no deben** confundirse con fallos reales: el umbral `http_req_failed` excluye los 400 esperados del caos; los fallos inesperados se rastrean en `telemetry_unexpected_failure`.

## Umbrales

- Menos del 5% de requests fallidos (global)
- p95 de latencia menor a 800 ms
- p95 de `telemetry_valid_request_duration` menor a 800 ms

## Verificación post-carga

```bash
# Duplicados no deben duplicar filas en telemetría
curl http://localhost:5000/api/fleet
curl http://localhost:5000/api/alerts
```
