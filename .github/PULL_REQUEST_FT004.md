# PR #27 — FT-004 consistencia read model

> Copiar este contenido al cuerpo del PR en GitHub antes del merge.

## Estado

**Head:** `27294ce0293cf5006a5326822b7dd994e7330c20`

**CI:** SUCCESS — [Actions run 29199182813](https://github.com/AlejoLobo/fleet-telemetry-platform/actions/runs/29199182813) (Backend, Integration, Worker, Web, Mobile, Docker, Infra, E2E)

**Base:** `develop`

---

## Resumen

Cierra **FT-004** (consistencia del read model) en seis commits sobre la rama `cursor/fix-ft-004-read-model-consistency-c166`. El PR unifica conectividad online/offline entre REST, polling, KafkaPush y SSE; corrige merge de parches en el dashboard; endurece migraciones y cursores; y hace confiable la expiración offline por lotes.

---

## Cambios por área

### Realtime y UoW
- Publica `vehicle-update` solo si el UPSERT en `fleet_vehicle_state` afectó filas (`rowsAffected > 0`).
- `LastSeenAt` realtime usa `telemetryEvent.Timestamp`.
- Fallo del publisher no revierte la transacción ya confirmada.
- Tras publicar: `online` → `MarkOnline`; `offline` → `MarkOfflinePublished` (marcadores durables en DB).

### Migraciones v2 / v3
- v2 atómica: DDL + backfill determinista + `schema_versions=2` en una transacción.
- v3 de verificación/reparación; omite backfill si v2 se aplicó en el mismo `InitializeAsync`.
- Advisory lock activo durante inicialización del Worker.
- Tablas nuevas: `fleet_connectivity_watermark`, `fleet_offline_publish_markers`.
- Índice keyset `ix_fleet_vehicle_state_last_timestamp_vehicle` creado tras existir `fleet_vehicle_state`.

### Expiración offline confiable
- `FleetConnectivityExpiryService` pagina la ventana `[previousThreshold, currentThreshold)` con keyset (`LastTimestamp ASC`, `VehicleId ASC`).
- `ConnectivityExpiryBatchSize` = tamaño de página; procesa todas las páginas antes de avanzar watermark.
- Watermark durable; si el publisher falla, el watermark no avanza; reintento omite ya publicados.
- Eliminados `FleetConnectivityExpiryState` y `FleetConnectivityPublishTracker` (memoria).

### Contrato y merge web
- `LastEventId` y `StatusEvaluatedAt` en REST, KafkaPush, expiración offline, tipos web, normalizador, `mergeVehicleUpdates` y `pruneVehiclePatches`.
- Orden de recencia: `LastSeenAt` → `LastEventId` → `StatusEvaluatedAt`.
- SSE offline evaluado después sobrevive a snapshot online más antiguo.

### Cursores y truncación
- Validación estricta de cursores (tamaño, propiedades desconocidas, keyset, `from`/`to`).
- Snapshot web con `partial` + `truncated`; analítica global vía `/api/ops/summary` cuando aplica.

### Panel de flota
- `FleetStatusPanel` recibe `aggregationSource`.
- Globales y badge **"agregados globales"** solo con `aggregationSource === "ops"`.
- Truncado sin Ops: "N vehículos mostrados", total global no disponible, métricas parciales del snapshot.

---

## Pruebas de regresión

| Área | Conteo |
|------|--------|
| Application | 159 |
| Integration | **105** |
| Worker | 34 |
| Web (Vitest) | **84** |
| Mobile typecheck | OK |

### Backend — expiración y tracker (nuevas)
- `Mas_de_batchSize_vehiculos_expiran_sin_perderse`
- `Vehiculos_con_mismo_timestamp_cruzan_varias_paginas`
- `Fallo_del_publisher_no_adelanta_watermark`
- `Reintento_no_duplica_los_ya_publicados`
- `Reinicio_despues_del_lookback_recupera_expiraciones_pendientes`
- `Ningun_vehiculo_queda_online_indefinidamente`
- `Evento_ya_expirado_se_publica_offline_una_sola_vez`
- `Evento_offline_no_limpia_su_marcador`
- `Evento_online_nuevo_limpia_marcador_anterior`
- `Expirador_no_duplica_evento_que_UoW_ya_publico_offline`

### Web — merge y panel (nuevas)
- `Offline_mismo_EventId_gana_a_snapshot_online_mas_antiguo`
- `Prune_conserva_offline_evaluado_despues`
- `Snapshot_offline_nuevo_elimina_parche_obsoleto`
- `Carrera_snapshot_iniciado_antes_de_expiracion_no_regresa_online`
- `Igual_EventId_e_igual_timestamp_no_depende_del_orden_de_render`
- `Panel_truncado_con_Ops_muestra_globales`
- `Panel_truncado_sin_Ops_no_inventa_total`
- `Panel_sin_Ops_no_muestra_agregados_globales`
- `Page_pasa_fuente_de_agregacion_al_panel`

---

## Commits incluidos

1. `0d6ac4d` — realtime sin regresión, migración v2, cursores, truncación web
2. `347118f` — contrato realtime, migración v3, analítica parcial
3. `1e67462` — conectividad unificada, merge SSE, analítica
4. `d226cad` — expiración offline básica, LastEventId, panel truncado
5. `4e02f8d` — expiración paginada, StatusEvaluatedAt, panel Ops, tracker
6. `27294ce` — fix DDL: índice de expiración tras `fleet_vehicle_state`

---

## Checklist FT-004

- [x] Ventana > batchSize no pierde vehículos (paginación keyset)
- [x] Error de publicación no pierde ventana (watermark conservado)
- [x] Reinicio no omite expiraciones (watermark + marcadores durables)
- [x] Snapshot online antiguo no elimina SSE offline (`StatusEvaluatedAt`)
- [x] `MarkOnline` solo para estado online
- [x] Panel no llama globales a métricas parciales sin Ops
- [x] Pruebas de regresión backend y web
- [x] GitHub Actions verde (Backend, Integration, Worker, Web, Mobile, Docker, Infra, E2E)
- [ ] Merge en `develop` (listo para revisión)
