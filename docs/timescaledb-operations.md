# TimescaleDB — operaciones de datos (MVP)

Políticas aplicadas por la migración `schema_versions` **v5** (`DatabaseInitializer`). Son una base defendible para telemetría de alta frecuencia; deben recalibrarse con métricas reales de tamaño de chunks, memoria y tasa de ingestión.

## Supuestos de volumen (MVP)

| Parámetro | Valor |
|-----------|--------|
| Vehículos | 1.000 |
| Frecuencia | 1 evento / 5 s por vehículo |
| Tasa aproximada | 200 eventos / s |
| Volumen diario aproximado | 17,28 millones de eventos / día |

## Políticas configuradas

| Recurso | Valor |
|---------|--------|
| Chunk interval `telemetry_events` | 6 horas |
| Compresión | Chunks con datos de más de **7 días** (`segmentby`: `VehicleId`, `orderby`: `Timestamp DESC`) |
| Retención cruda `telemetry_events` | **90 días** (no afecta `fleet_vehicle_state`, `fleet_alerts`, `fleet_alert_states`, agregados ni tablas de configuración) |
| Idempotencia `processed_events` | Conservación **120 días**; limpieza por lotes (máx. 100.000 filas) cada **5 minutos** |
| Agregado continuo `telemetry_hourly` | Bucket 1 h por `VehicleId`; refresh cada **15 minutos** desde **30 días** atrás hasta **10 minutos** antes del presente (`WITH NO DATA` al crear) |

### Propósito de `telemetry_hourly`

Vista materializada continua mínima para analítica operacional (conteo, velocidades y mínimos de combustible/batería por hora y vehículo). En este MVP **no** se exponen endpoints ni se cambian consultas de aplicación; solo se prepara la base en TimescaleDB.

## Idempotencia

Reejecutar `DatabaseInitializer` no debe duplicar:

- políticas de compresión / retención;
- política de refresh de `telemetry_hourly`;
- jobs `cleanup_processed_events`;
ni fallar por objetos existentes (`if_not_exists` + comprobaciones de catálogo).

## Consultas operativas

### Hypertables

```sql
SELECT hypertable_schema, hypertable_name, compression_enabled
FROM timescaledb_information.hypertables
ORDER BY 1, 2;
```

### Chunks / intervalo

```sql
SELECT hypertable_name, column_name, time_interval
FROM timescaledb_information.dimensions
WHERE hypertable_name = 'telemetry_events';

SELECT chunk_name, range_start, range_end, is_compressed
FROM timescaledb_information.chunks
WHERE hypertable_name = 'telemetry_events'
ORDER BY range_start DESC
LIMIT 20;
```

### Compresión

```sql
SELECT * FROM timescaledb_information.compression_settings
WHERE hypertable_name = 'telemetry_events';

SELECT job_id, proc_name, schedule_interval, config
FROM timescaledb_information.jobs
WHERE proc_name = 'policy_compression'
  AND hypertable_name = 'telemetry_events';
```

### Retención

```sql
SELECT job_id, proc_name, schedule_interval, config
FROM timescaledb_information.jobs
WHERE proc_name = 'policy_retention'
  AND hypertable_name = 'telemetry_events';
```

### Continuous aggregate

```sql
SELECT view_name, materialization_hypertable_name, compression_enabled
FROM timescaledb_information.continuous_aggregates
WHERE view_name = 'telemetry_hourly';

SELECT j.job_id, j.proc_name, j.schedule_interval, j.config
FROM timescaledb_information.jobs j
JOIN timescaledb_information.continuous_aggregates c
  ON c.materialization_hypertable_name = j.hypertable_name
WHERE j.proc_name = 'policy_refresh_continuous_aggregate'
  AND c.view_name = 'telemetry_hourly';
```

### Cleanup `processed_events`

```sql
SELECT job_id, proc_name, schedule_interval, next_start
FROM timescaledb_information.jobs
WHERE proc_name = 'cleanup_processed_events';

-- Ejecución manual de un lote
SELECT cleanup_processed_events(0, '{}'::jsonb);
```
