# Fleet Telemetry Mobile (Fase 5)

App React Native Expo para conductores con cola offline-first.

## Stack

- Expo 52 + React Native
- SQLite (`expo-sqlite`) para cola local
- NetInfo para detectar conectividad
- `expo-location` con fallback a coordenadas simuladas

## Configuración

```bash
cp .env.example .env
```

| Variable | Descripción |
|----------|-------------|
| `EXPO_PUBLIC_API_URL` | Backend .NET (default `http://localhost:5000`) |
| `EXPO_PUBLIC_VEHICLE_ID` | Vehículo por defecto |
| `EXPO_PUBLIC_DRIVER_ID` | Conductor por defecto |

> En dispositivo físico o emulador Android, usa la IP de tu máquina en lugar de `localhost`.

## Comandos

```bash
cd mobile
npm install
npx expo start
```

## Funcionalidades

- Captura periódica de telemetría (GPS o simulado)
- `EventId` generado en cliente (`expo-crypto`)
- Persistencia local en SQLite si no hay red
- Sincronización batch vía `POST /api/telemetry/batch` al reconectar
- Reintento individual si falla un batch
- UI mínima de conductor: tracking, captura manual, sync

## Requisitos

- Node.js 18+
- Expo Go o emulador Android/iOS
- Backend en ejecución para sync en vivo
