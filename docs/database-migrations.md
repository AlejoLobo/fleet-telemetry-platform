# Migraciones de base de datos (TimescaleDB)

## Enfoque actual (MVP)

| Entorno | DDL automático | Advisory lock |
|---------|----------------|---------------|
| Development | Sí (`DatabaseInitializer` al arrancar el Worker) | Sí (`pg_advisory_lock`) |
| Production / Staging | **No** (salvo flag explícito) | No |

El Worker evalúa `DatabaseInitializationPolicy`:

- Ejecuta DDL si `IHostEnvironment.IsDevelopment()` **o** `TimescaleDb:AllowAutoSchemaInitialization=true`.
- En producción el valor por defecto es **no ejecutar DDL**; el esquema debe aplicarse con migraciones controladas.

## Tabla `schema_versions`

`DatabaseInitializer` registra la versión aplicada:

| Columna | Descripción |
|---------|-------------|
| `Version` | Entero incremental (PK) |
| `AppliedAt` | Timestamp de aplicación |
| `Description` | Resumen legible del cambio |

Versión inicial: `1` — esquema base con hypertable `telemetry_events`.

Versión `2` — tabla de lectura `fleet_vehicle_state` (un registro por vehículo), índices de paginación e historial, backfill determinista desde `telemetry_events`.

Versión `3` — verificación y reparación del read model para instalaciones que ya tenían v2 registrada. En una **instalación nueva** (v2 aplicada en el mismo `InitializeAsync`), v3 **no repite** el backfill completo; solo registra la versión. En una **instalación heredada** (v2 previa, v3 ausente), v3 ejecuta el backfill reparador.

Versión `4` — tabla `fleet_alert_states` (estado activo / cooldown por `VehicleId` + `AlertType`). No borra `fleet_alerts` históricas; no requiere backfill.

Versión `5` — mantenimiento TimescaleDB sobre `telemetry_events` / `processed_events`: chunk interval 6 h, compresión (>7 días), retención cruda 90 días, agregado continuo `telemetry_hourly` (refresh 15 min), índice `ix_processed_events_processed_at` y job `cleanup_processed_events` (120 días, lotes de 100.000 cada 5 min). Ver [timescaledb-operations.md](timescaledb-operations.md).

Versión `6` — registro de dispositivos `fleet_devices` (`device_id` UUID PK, `vehicle_name` UNIQUE) y secuencia `fleet_vehicle_name_seq` para asignación atómica de nombres `VH-###`. El `DeviceId` es identidad estable; el nombre es editable y no redefine la identidad.

### Política v2 + v3 (sin doble backfill)

```
InitializeAsync:
  v2AppliedNow = ApplyReadModelMigrationV2Async()
  ApplyReadModelVerificationV3Async(v2AppliedNow)
  ApplyAlertStateMigrationV4Async()
  ApplyTimescaleMaintenanceMigrationV5Async()
  ApplyFleetDevicesMigrationV6Async()
```

| Escenario | Backfills ejecutados |
|-----------|---------------------|
| Instalación nueva | 1 (solo v2) |
| v2 heredada, v3 pendiente | 1 (reparación v3) |
| v2 y v3 ya registradas | 0 |

`DatabaseInitializer` usa **advisory lock** (`pg_advisory_lock`) en cualquier entorno que habilite inicialización automática.

### `fleet_vehicle_state`

| Campo | Descripción |
|-------|-------------|
| `VehicleId` | PK |
| `LastEventId`, `LastTimestamp` | Último evento aplicado al estado |
| `Latitude`, `Longitude`, `SpeedKmh`, … | Snapshot operativo |
| `LocationSource` | `gps` / `simulated` del último evento |
| `UpdatedAt` | Momento de UPSERT |

Actualización transaccional en el Worker (misma transacción que `telemetry_events`, `processed_events`, `fleet_alerts`). UPSERT con protección ante eventos fuera de orden:

- Solo reemplaza si `incoming.Timestamp > stored.LastTimestamp`, o mismo timestamp y `EventId` mayor.

Backfill idempotente en `DatabaseInitializer` (`ORDER BY VehicleId, Timestamp DESC, EventId DESC`).

## Ruta recomendada con EF Core Migrations

Para producción, migrar del DDL en código a migraciones versionadas:

```bash
cd backend

# Herramienta global (una vez)
dotnet tool install --global dotnet-ef

# Crear migración desde el modelo EF
dotnet ef migrations add InitialTimescaleSchema \
  --project FleetTelemetry.Infrastructure \
  --startup-project FleetTelemetry.Worker \
  --output-dir Persistence/Migrations

# Generar script SQL (revisar hypertable/extension manualmente)
dotnet ef migrations script \
  --project FleetTelemetry.Infrastructure \
  --startup-project FleetTelemetry.Worker \
  -o ../../scripts/migrations/001-initial.sql

# Aplicar en entorno controlado (CI/CD o DBA)
dotnet ef database update \
  --project FleetTelemetry.Infrastructure \
  --startup-project FleetTelemetry.Worker
```

### Limitaciones conscientes

- **TimescaleDB** requiere `CREATE EXTENSION timescaledb` y `create_hypertable` fuera del alcance estándar de EF; incluir en scripts SQL o post-migración.
- RDS PostgreSQL **no** incluye la extensión TimescaleDB; usar [Timescale Cloud](https://www.timescale.com/) o instancia self-hosted. Ver `infra/README.md`.
- No habilitar `AllowAutoSchemaInitialization` en producción salvo bootstrap controlado de un entorno efímero.

## Variables de configuración

```json
{
  "TimescaleDb": {
    "ConnectionString": "Host=...;Database=fleet;...",
    "AllowAutoSchemaInitialization": false
  }
}
```

## Próximos pasos

1. Extraer DDL de `DatabaseInitializer` a migraciones EF + scripts SQL para hypertables.
2. Pipeline CI que valide scripts con TimescaleDB en contenedor.
3. Job de despliegue que consulte `schema_versions` antes de aplicar cambios.
