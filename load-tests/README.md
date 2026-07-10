# Pruebas de carga k6 — ingesta de telemetría

## Distribución (una sola variable aleatoria)

| Rango | Porcentaje | Comportamiento |
|-------|------------|----------------|
| `[0.00, 0.05)` | 5 % | Payload inválido intencional → espera **400** |
| `[0.05, 0.15)` | 10 % | Duplicado: **mismo payload completo** del pool → espera **202** |
| `[0.15, 1.00)` | 85 % | Evento nuevo válido → espera **202** |

Los duplicados reutilizan EventId, vehículo, timestamp, coordenadas y métricas (pool de payloads completos).

## Requisitos

- [k6](https://k6.io/docs/get-started/installation/) instalado
- API en ejecución (`http://localhost:5000` por defecto)

## Ejecución

```bash
cd load-tests
k6 run telemetry-ingest.js
```

Con variables:

```bash
k6 run \
  -e API_URL=http://localhost:5000 \
  -e VUS=10 \
  -e DURATION=30s \
  -e VEHICLES=300 \
  telemetry-ingest.js
```

Con auth:

```bash
k6 run -e AUTH_TOKEN=<jwt> telemetry-ingest.js
```

## Thresholds

- `telemetry_unexpected_failure_rate` < 1 %
- `http_req_duration` p95 < 800 ms, p99 < 1500 ms
- `telemetry_valid_request_duration` p95/p99
- `telemetry_valid_accepted_rate` > 95 %
- `telemetry_invalid_rejected_rate` > 95 %

Los 400 intencionales se registran como respuestas esperadas (`http.expectedStatuses(202, 400)`), no como fallos HTTP globales.
