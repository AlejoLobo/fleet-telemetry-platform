# Analítica operativa

## Estado actual

Las consultas analíticas del MVP usan **TimescaleDB** (`TimescaleAnalyticsQueryService`).

- Velocidad promedio por vehículo y rango de fechas
- KPIs del dashboard calculados en el cliente o vía API de flota/telemetría

## Contrato estable

Application solo depende de `IAnalyticsQueryService`:

```csharp
Task<double> GetAverageSpeedAsync(string vehicleId, DateTimeOffset from, DateTimeOffset to, ...);
```

El dashboard y el agente IA consumen esta interfaz; el origen de datos es intercambiable.

## Druid (futuro)

Apache Druid quedó planificado para agregaciones OLAP a gran escala. No hay implementación activa.

Pasos para integrarlo:

1. Desplegar Druid (Docker Compose o servicio gestionado)
2. Implementar `DruidAnalyticsQueryService : IAnalyticsQueryService`
3. Registrar en `DependencyInjection.cs` en lugar de `TimescaleAnalyticsQueryService`
