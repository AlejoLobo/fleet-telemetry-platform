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

## EAS Preview (Android APK)

Build de preview **manual** vía GitHub Actions. No publica en Play Store ni App Store.

### Prerrequisito (una vez por proyecto)

1. Crear cuenta en [expo.dev](https://expo.dev).
2. Vincular el proyecto local con EAS (genera `extra.eas.projectId` en `app.json`):

```bash
cd mobile
npx eas-cli login
npx eas init
```

3. Configurar el secret `EXPO_TOKEN` en GitHub:
   - Expo → **Account settings** → **Access tokens** → **Create token**
   - GitHub repo → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**
   - Nombre: `EXPO_TOKEN`, valor: el token de Expo

### Lanzar el workflow

1. GitHub → **Actions** → **Mobile Preview Build**
2. **Run workflow** → branch `main`
3. Opcional: `api_url` (default `http://localhost:5000`; en dispositivo físico usa la IP de tu máquina, p. ej. `http://192.168.1.10:5000`)
4. Esperar a que termine el job (EAS compila en la nube)

### Artefacto producido

| Campo | Valor |
|-------|-------|
| Tipo | APK Android (`buildType: apk`) |
| Perfil | `preview` en `eas.json` |
| Distribución | `internal` (descarga directa, sin tiendas) |
| Dónde descargar | [expo.dev](https://expo.dev) → proyecto **fleet-telemetry-mobile** → **Builds** |

La lógica offline-first (SQLite, cola local, sync batch) no cambia; el APK empaqueta la misma app Expo 52.

### Build local con EAS (opcional)

```bash
cd mobile
eas build --platform android --profile preview
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
