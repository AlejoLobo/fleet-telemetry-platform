# Fleet Telemetry Dashboard (Fase 4)

Dashboard Next.js para monitoreo de flotas en tiempo real.

## Stack

- Next.js 15 (App Router)
- React 19 + TypeScript
- Tailwind CSS
- Componentes estilo shadcn/ui

## Configuración

Copia `.env.example` a `.env.local`:

```bash
NEXT_PUBLIC_API_URL=http://localhost:5000
NEXT_PUBLIC_USE_MOCK=false
```

| Variable | Descripción |
|----------|-------------|
| `NEXT_PUBLIC_API_URL` | URL del backend .NET |
| `NEXT_PUBLIC_USE_MOCK=true` | Usa datos mock sin backend |

El mapa usa **Leaflet** con tiles de **OpenStreetMap** (sin API key).

## Comandos

```bash
cd web
npm install
npm run dev
```

Abre http://localhost:3000

## Funcionalidades

- Estado de flota y mapa de coordenadas
- Alertas en tiempo real vía SSE
- Tabla de telemetría por vehículo
- Chat con agente IA operativo
- Resumen analítico (TimescaleDB / mock Druid)
- Fallback a mock si el backend no está disponible

## Requisitos

- Node.js 18+
- Backend en `http://localhost:5000` (API + Worker + Docker)
