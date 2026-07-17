# Checklist operativo de release

Usar este documento para cada release estable. Marcar solo lo realmente ejecutado.

## Precondiciones Git

- [ ] Working tree limpio (`git status`)
- [ ] Rama `release/X.Y.Z` creada desde `develop` actualizado
- [ ] Sin force-push salvo política explícita de reescritura acordada
- [ ] No existe el tag `vX.Y.Z` antes del merge a `main`

## Versionado

- [ ] Versión web = `X.Y.Z` (`web/package.json` + lockfile)
- [ ] Versión mobile = `X.Y.Z` (`mobile/package.json`, `app.json`, lockfile)
- [ ] Versión .NET central (`backend/Directory.Build.props` o equivalente)
- [ ] Lockfiles regenerados con el gestor de paquetes (no editados a mano)
- [ ] Expo SDK y package/bundle identifiers sin cambios no intencionados

## Documentación

- [ ] `CHANGELOG.md` actualizado
- [ ] `docs/releases/vX.Y.Z.md` creado
- [ ] `docs/releases/vX.Y.Z-validation.md` con resultados reales
- [ ] README referencia la versión estable preparada
- [ ] Sin afirmaciones de HA / exactly-once / TLS productivo / tiendas / Druid real
- [ ] Stack documentado: .NET 10, Next 15, React 19, Expo 54, KafkaPush por defecto

## Backend

- [ ] `dotnet restore backend/FleetTelemetry.sln`
- [ ] `dotnet build … --configuration Release`
- [ ] `dotnet test` Application.Tests
- [ ] `dotnet test` Worker.Tests
- [ ] `dotnet test` Integration.Tests

## Web

- [ ] `npm ci`
- [ ] `npm run lint`
- [ ] `npm run typecheck`
- [ ] `npm run test:ci`
- [ ] `npm run build`

## Mobile

- [ ] `npm ci`
- [ ] `npm run typecheck`
- [ ] `npm run test:ci`
- [ ] `npm run export` (o script equivalente del repo)

## Docker

- [ ] `docker compose config --quiet`
- [ ] `docker compose build api worker web`
- [ ] `docker compose --profile app up -d --build`
- [ ] Smoke (`scripts/smoke-test.sh` o `.ps1`)
- [ ] `docker compose --profile app down` (sin `-v` salvo justificación)

## Carga

- [ ] k6 con script documentado (`load-tests/telemetry-smoke.js` y/o `telemetry-ingest.js`) en modo reducido

## Terraform

- [ ] `terraform fmt -check -recursive infra/terraform`
- [ ] `terraform init -backend=false` + `validate` en blueprint y `dev`
- [ ] Sin `terraform apply` en el flujo de release

## Seguridad

- [ ] Búsqueda de secretos / tokens / passwords versionados
- [ ] Placeholders legítimos (`CHANGE_ME`, ejemplos) documentados
- [ ] Sin credenciales reales en Git

## CI y merge

- [ ] Workflow CI aprobado en el PR `release/*` → `main`
- [ ] PR fusionado a `main`
- [ ] CI de `main` aprobado post-merge
- [ ] Tag anotado `vX.Y.Z` creado desde `main` (no desde `develop`)
- [ ] Tag empujado a `origin`
- [ ] GitHub Release publicado (estable)
- [ ] `main` sincronizado hacia `develop` (`merge --no-ff`)
