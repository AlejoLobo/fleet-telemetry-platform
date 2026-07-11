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
