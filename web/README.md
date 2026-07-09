# Fleet Telemetry Dashboard (Fase 4)

Dashboard Next.js para monitoreo de flotas en tiempo real.

## Stack

- Next.js 15 (App Router)
- React 19 + TypeScript
- Tailwind CSS
- Leaflet + OpenStreetMap

## Configuración

```bash
cp .env.example .env.local
```

| Variable | Descripción |
|----------|-------------|
| `NEXT_PUBLIC_API_URL` | URL del backend .NET (default `http://localhost:5000`) |

## Comandos

```bash
cd web
npm install
npm run dev
```

Abre http://localhost:3000

## Modos de datos

- **Tiempo real** — consume la API y SSE del backend.
- **Demo** — genera vehículos aleatorios en el cliente (sin backend).

## Funcionalidades

- Mapa de flota con iconos y ajuste a calles (OSRM)
- KPIs, alertas, telemetría y chat con agente IA
- Confirmación de alertas (modo API)

## Requisitos

- Node.js 18+
- Backend en `http://localhost:5000` (Docker + API + Worker) para modo tiempo real
