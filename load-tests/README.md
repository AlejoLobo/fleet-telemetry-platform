# Pruebas de carga k6 (Fase 6)

Simula ingesta concurrente contra `POST /api/telemetry`.

## Requisitos

- [k6](https://k6.io/docs/get-started/installation/) instalado
- API + Worker + Docker (Kafka, TimescaleDB) en ejecución

## Ejecución

```bash
cd load-tests

# Carga básica
k6 run telemetry-ingest.js

# Personalizada
k6 run -e API_URL=http://localhost:5000 -e VUS=25 -e DURATION=1m telemetry-ingest.js

# Con JWT (si Auth:Enabled=true)
k6 run -e AUTH_TOKEN=<jwt> telemetry-ingest.js
```

## Umbrales

- Menos del 5% de requests fallidos
- p95 de latencia menor a 800 ms
