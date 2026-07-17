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

Las pruebas Jest simulan `PRAGMA table_info` y **no** sustituyen una prueba nativa de `expo-sqlite` / `NativeDatabase`.

## Procedimiento de validación en Android (manual obligatorio)

### Pasos

1. Instalar una APK o development build **anterior** sin la columna `device_id`.
2. Abrir la aplicación.
3. Generar al menos **cinco** eventos offline (tracking sin red o API bloqueada).
4. Anotar: DeviceId local, cantidad de pendientes en UI.
5. Cerrar la aplicación (no desinstalar).
6. Actualizar con la build nueva **sobre la instalación existente** (sin borrar datos).
7. Abrir la nueva versión.
8. Confirmar que **no** aparece el error `table telemetry_queue has no column named device_id`.
9. Confirmar que la UI conserva la cantidad de pendientes (±0 por captura nueva).
10. Recuperar red / auth y sincronizar; confirmar envío.
11. Confirmar que los eventos activos quedan con `device_id` = UUID local.
12. Reiniciar la app; confirmar que no falla la migración y la cola permanece coherente.

### Plantilla de evidencia (sin datos sensibles)

| Campo | Valor |
|-------|-------|
| Fecha de validación | _pendiente_ |
| Dispositivo / emulador | _pendiente_ |
| Versión Android | _pendiente_ |
| Build antigua (commit / versionCode) | _pendiente_ |
| Build nueva (commit / versionCode) | _pendiente_ |
| Eventos pendientes antes | _pendiente_ |
| Eventos pendientes después | _pendiente_ |
| Resultado sync | _pendiente_ |
| Error `device_id` reapareció | No / Sí |

### Estado en este entorno de desarrollo

```text
Validación nativa Android: MANUAL PENDIENTE
```

No hay dispositivo Android ni emulador disponible en este entorno. El procedimiento queda listo; **no** se afirma ejecución nativa aquí.

### Limpieza de cache de desarrollo

```bash
cd mobile
npx expo start -c
npx expo export --clear
```

Si se usa APK o development build, reconstruir e **reinstalar/actualizar** la app.
