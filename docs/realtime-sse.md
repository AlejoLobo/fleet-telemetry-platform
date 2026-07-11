# Tiempo real SSE — decisión MVP

Índice: [README.md](README.md) · Arquitectura: [architecture.md](architecture.md).

## Qué hay hoy

El dashboard consume `GET /api/events/stream` (Server-Sent Events). Los eventos (`fleet-update`, `alert`, `heartbeat`) no salen de Kafka ni del Worker: los genera `FleetSsePollerHostedService`, un `BackgroundService` en la API que:

1. Consulta TimescaleDB (estado de flota + alertas abiertas).
2. Compara un hash del snapshot de flota para evitar broadcasts redundantes.
3. Publica en `FleetSseBroker` solo cuando hay cambios (o heartbeat cada 15s).

Intervalos configurables (`Sse` en `appsettings.json` / env):

| Modo | Default | Cuándo |
|------|---------|--------|
| Activo | **3 segundos** | La flota cambió en el ciclo anterior |
| Idle | **10 segundos** | Sin cambios (menos carga en DB) |

Si no hay suscriptores SSE, el poller espera el intervalo activo y no consulta la DB.

## Por qué es una decisión MVP (consciente)

- Entrega tiempo real usable en el dashboard sin acoplar el Worker a HTTP/SSE.
- Evita un segundo consumidor Kafka solo para UI en esta fase.
- El hash reduce ruido: no reenvía el mismo snapshot cada 3s.
- Trade-off aceptado: latencia acotada al intervalo de polling (~3s en activo) y carga de lectura en TimescaleDB proporcional a suscriptores activos.

**No es** push event-driven extremo Kafka → cliente. Es polling + fan-out SSE, suficiente para demostrar la vertical.

## Alternativas productivas (no implementadas)

Cuando el volumen o la latencia lo exijan, sin reescribir el contrato SSE del cliente:

### 1. Worker publica evento directo al broker SSE

Tras persistir telemetría/alertas, el Worker (o un bus interno compartido) notifica al mismo `FleetSseBroker` / canal de pub-sub.

- **Pros:** latencia baja; menos lecturas periódicas a DB.
- **Contras:** Worker y API deben compartir infraestructura de mensajería in-process o Redis/SignalR; más acoplamiento operativo.

### 2. Consumidor Kafka dedicado alimenta el broker SSE

Un servicio (o proceso en la API) consume `telemetry.raw` (o un tópico de “fleet-changed”) y traduce mensajes a eventos SSE.

- **Pros:** desacopla persistencia de UI; escala con particiones Kafka; alineado al pipeline event-driven.
- **Contras:** otro consumidor/grupo; hay que definir payload de “cambio de flota” vs reconsultar DB.

En ambos casos el endpoint `GET /api/events/stream` y los tipos de evento pueden mantenerse; solo cambia la **fuente** que alimenta el broker.


## Autenticación segura para SSE

`EventSource` nativo del navegador **no permite** enviar cabeceras personalizadas (p. ej. `Authorization: Bearer …`). Por eso **no se debe** pasar el JWT en query string (`?token=…`): queda en logs de proxy, historial del navegador y referrers.

### Enfoque recomendado (producción)

1. **Ticket de corta duración (preferido)**  
   - El cliente autenticado llama `POST /api/auth/sse-ticket` con JWT en cabecera.  
   - La API devuelve un ticket opaco de un solo uso, TTL ~60 s, ligado al usuario y al endpoint SSE.  
   - El cliente abre SSE con `fetch` + `ReadableStream` (o polyfill) y envía `Authorization: Bearer <ticket>` o `X-Sse-Ticket: <ticket>`.  
   - El ticket se invalida al cerrar la conexión o al expirar.

2. **Cabecera vía fetch-stream (sin ticket)**  
   - Usar `fetch('/api/events/stream', { headers: { Authorization: 'Bearer …' } })` y parsear el stream manualmente.  
   - Válido cuando el token JWT tiene vida corta y rotación controlada; menos seguro que ticket de un solo uso.

### Estado MVP

- Con `Auth:Enabled=false` (default), SSE permanece abierto para demo local.  
- Con `Auth:Enabled=true`, el endpoint exige política `FleetRead` vía `AuthorizeWhenEnabled`; el dashboard debe migrar a **fetch-stream + cabecera** o al flujo de **sse-ticket** antes de producción.  
- No implementar `?access_token=` en la URL.

## Qué no se hace ahora

- No se reescribe la arquitectura SSE completa.
- No se introduce Redis/SignalR/WebSockets en este MVP.
- No se elimina el poller hasta que una alternativa productiva esté cableada.
