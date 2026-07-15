# Migración SQLite offline (`device_id`) en instalación existente

## Síntoma original

```text
Call to function 'NativeDatabase.prepareAsync' has been rejected.
Caused by: Error code: table telemetry_queue has no column named device_id
```

Causa: `CREATE TABLE IF NOT EXISTS` no añade columnas a tablas ya creadas.

## Comportamiento actual

1. Apertura y migración se ejecutan **una sola vez** por proceso (`readyDbPromise`).
2. La migración inspecciona `PRAGMA table_info(telemetry_queue)`.
3. Si falta `device_id`: `ALTER TABLE telemetry_queue ADD COLUMN device_id TEXT`.
4. Índices y `schema_meta` / `PRAGMA user_version` = **5**.
5. Backfill de eventos activos vía `migratePendingEventsToDeviceId` (no toca `synced` / `permanent_failure`).

Las pruebas Jest simulan `PRAGMA table_info` y no sustituyen una prueba nativa de `expo-sqlite`.

## Cómo validar en Android físico / emulador

1. Instalar una build anterior **sin** `device_id` (o borrar solo el esquema no es aceptable en prod; use APK legacy).
2. Generar telemetría offline para llenar la cola.
3. Instalar la build nueva **sin** desinstalar (actualizar sobre la existente).
4. Abrir la app: no debe aparecer el error `no column named device_id`.
5. Comprobar pendientes UI y sync.

Limpieza de bundle/cache de desarrollo:

```bash
cd mobile
npx expo start -c
npx expo export --clear
```

Si usa APK o development build, reconstruir e **reinstalar** (o actualizar) la app: un binario antiguo en el dispositivo puede seguir mostrando UI o schema cacheados.
