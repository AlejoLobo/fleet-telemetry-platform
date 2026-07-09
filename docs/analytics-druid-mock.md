# Analítica — Mock de Druid (MVP)

En el MVP, `IAnalyticsQueryService` está implementado por **`TimescaleAnalyticsQueryService`**, que calcula agregaciones simples (velocidad promedio) directamente sobre TimescaleDB.

## Por qué no hay Druid real

Apache Druid es un sistema OLAP distribuido pensado para agregaciones masivas en producción. Para esta prueba técnica:

- **Fase 2–3:** consultas analíticas básicas vía TimescaleDB (suficiente para demo).
- **Producción:** reemplazar `TimescaleAnalyticsQueryService` por `DruidAnalyticsQueryService` que consulte un cluster Druid vía SQL/JSON API.

## Contrato estable

La capa Application solo depende de `IAnalyticsQueryService`:

```csharp
Task<double> GetAverageSpeedAsync(string vehicleId, DateTimeOffset from, DateTimeOffset to, ...);
```

El dashboard y el agente IA consumen esta interfaz — el origen de datos es intercambiable.

## Identificación en logs

Las respuestas del agente IA indican: *"Fuente: TimescaleDB (mock de Druid en MVP)"*.

## Migración a Druid (blueprint)

1. Desplegar Druid en Docker Compose o AWS (ver `infra/` Fase 6).
2. Crear datasource `telemetry_events` con dimensiones `vehicleId`, métrica `speedKmh`.
3. Implementar `DruidAnalyticsQueryService : IAnalyticsQueryService`.
4. Registrar en `DependencyInjection.cs` (perfil Api) en lugar de `TimescaleAnalyticsQueryService`.
