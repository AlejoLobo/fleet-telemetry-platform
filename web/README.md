# Fleet Telemetry Dashboard

Dashboard Next.js para monitoreo de flotas en tiempo real.

Quickstart global del monorepo: [../docs/getting-started.md](../docs/getting-started.md) · [../README.md](../README.md).

## Stack

- Next.js 15 (App Router)
- React 19 + TypeScript
- Tailwind CSS
- Leaflet + OpenStreetMap
- Vitest + Testing Library

## Configuración

```bash
cp .env.example .env.local
```

| Variable | Descripción |
|----------|-------------|
| `NEXT_PUBLIC_API_URL` | Backend .NET (default `http://localhost:5000`) |
| `NEXT_PUBLIC_E2E_TEST_MODE` | Solo pruebas E2E (`true` activa inyector y Demo sembrado). No usar en producción. |
| `NEXT_PUBLIC_E2E_SEED` | Semilla Demo E2E (default `12345`) |

## Comandos

```bash
cd web
npm ci
npm run lint
npm run typecheck
npm run test:ci
npm run build
npm run dev
```

Abre http://localhost:3000

## Modos de datos

- **Tiempo real** — consume la API y SSE del backend.
- **Demo** — genera vehículos aleatorios en el cliente (sin backend).

## Tiempo real (SSE)

- **KafkaPush** es el modo predeterminado del backend (eventos canónicos como `vehicle-update`).
- **Polling** es un modo alternativo configurable en el servidor.
- El cliente Web cubre replay/`Last-Event-ID`, `stream-reset`, resync por snapshot y protección contra cargas obsoletas.
- El mapa y el estado de vehículos se actualizan por SSE.
- La tabla histórica se actualiza mediante carga inicial, selección, actualización manual o resync.
- Detalle de contrato: [../docs/realtime-sse.md](../docs/realtime-sse.md).

## Funcionalidades

- Mapa de flota con iconos y ajuste a calles (OSRM)
- KPIs, alertas, telemetría y chat con agente IA
- Confirmación de alertas (modo API)

## Pruebas y cobertura

`npm run test:ci` ejecuta Vitest con cobertura V8 (`text` + `json-summary`). No hay umbral porcentual global obligatorio.

Suites representativas:

| Área | Archivo |
|------|---------|
| Hooks de datos / cargas obsoletas | `src/hooks/use-fleet-data.test.tsx` |
| SSE, auth, replay, stream-reset, offsets 64-bit | `src/hooks/use-sse-stream.test.tsx` |
| Headers SSE / Last-Event-ID | `src/lib/sse-fetch-client.test.ts` |
| Parser / reconnect / resync | `src/lib/sse-parser.test.ts`, `sse-reconnect.test.ts`, `sse-resync.test.ts` |
| Paginación | `src/lib/fleet-pagination.test.ts` |
| Integración dashboard resync | `src/app/dashboard-sse-resync.test.tsx` |

El directorio `coverage/` está ignorado por Git.

## Requisitos

- Node.js 18+
- Backend en `http://localhost:5000` (Docker + API + Worker) para modo tiempo real
