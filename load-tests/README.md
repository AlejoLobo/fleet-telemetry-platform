# Pruebas de carga k6 — ingesta de telemetría

## Distribución (una sola variable aleatoria)

| Rango | Porcentaje | Comportamiento |
|-------|------------|----------------|
| `[0.00, 0.05)` | 5 % | Payload inválido intencional → espera **400** |
| `[0.05, 0.15)` | 10 % | Duplicado real: **mismo payload completo** sembrado en `setup()` → espera **202** |
| `[0.15, 1.00)` | 85 % | Evento nuevo válido → espera **202** |

Los duplicados reutilizan exactamente el mismo `eventId`, vehículo, timestamp, coordenadas y métricas. En `setup()` se envían los 50 payloads a la API (cada uno debe responder **202**) antes de la fase de carga; el 10 % del tráfico reenvía esos mismos cuerpos.

## Expectativas HTTP por solicitud

- Inválidos: `responseCallback: http.expectedStatuses(400)` — un 202 o 5xx cuenta como fallo inesperado.
- Válidos y duplicados: `responseCallback: http.expectedStatuses(202)` — un 400 en una solicitud válida cuenta como fallo inesperado.
- No hay configuración global que trate 202 y 400 como esperados a la vez.

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

## Métricas custom

- `telemetry_accepted` — eventos válidos aceptados (202)
- `telemetry_duplicate_sent` — duplicados reales enviados
- `telemetry_intentional_invalid` — inválidos intencionales enviados
- `telemetry_unexpected_failure_rate` — respuestas distintas a la expectativa por tipo de solicitud
- `telemetry_valid_accepted_rate` — tasa de 202 en válidos/duplicados
- `telemetry_invalid_rejected_rate` — tasa de 400 en inválidos
