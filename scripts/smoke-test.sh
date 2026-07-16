#!/usr/bin/env bash
# Smoke test end-to-end de la demo local (API → Kafka → Worker → TimescaleDB → consulta + DLQ).
# Requiere: docker compose --profile app up -d --build
set -u

API_BASE="${FLEET_API_URL:-http://localhost:5000}"
API_BASE="${API_BASE%/}"
WAIT_SECONDS=6
DLQ_TIMEOUT=12

API_OK="FAIL"
SEND_OK="FAIL"
PROCESS_OK="FAIL"
DLQ_OK="FAIL"

step() { printf '\n==> %s\n' "$1"; }
ok() { printf '%s: OK\n' "$1"; }
fail() { printf '%s: FAIL%s\n' "$1" "${2:+ ($2)}"; }

check_api() {
  step "Verificando API en ${API_BASE}/health/live"
  local body
  if body=$(curl -fsS --max-time 5 "${API_BASE}/health/live" 2>/dev/null) && echo "$body" | grep -q 'alive'; then
    API_OK="OK"
    ok "API disponible"
    return 0
  fi
  fail "API disponible"
  return 1
}

send_valid() {
  EVENT_ID="$(uuidgen 2>/dev/null || cat /proc/sys/kernel/random/uuid 2>/dev/null || echo "$(date +%s)-$RANDOM")"
  DEVICE_ID="$(uuidgen 2>/dev/null || cat /proc/sys/kernel/random/uuid 2>/dev/null || echo "aaaaaaaa-bbbb-4ccc-8ddd-$(date +%s | tail -c 13)")"
  TIMESTAMP="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

  step "Registrando dispositivo deviceId=${DEVICE_ID}"
  local reg_code
  reg_code=$(curl -sS -o /tmp/fleet-smoke-register.json -w "%{http_code}" --max-time 10 \
    -X POST "${API_BASE}/api/devices/register" \
    -H "Content-Type: application/json" \
    -H "X-Device-Id: ${DEVICE_ID}" \
    -d "{\"deviceId\": \"${DEVICE_ID}\"}" 2>/dev/null || echo "000")
  if [ "$reg_code" != "200" ]; then
    fail "Evento válido enviado" "registro HTTP ${reg_code}"
    return 1
  fi

  step "Enviando evento válido deviceId=${DEVICE_ID}"
  local code
  code=$(curl -sS -o /tmp/fleet-smoke-ingest.json -w "%{http_code}" --max-time 10 \
    -X POST "${API_BASE}/api/telemetry" \
    -H "Content-Type: application/json" \
    -H "X-Device-Id: ${DEVICE_ID}" \
    -d "{
      \"eventId\": \"${EVENT_ID}\",
      \"deviceId\": \"${DEVICE_ID}\",
      \"driverId\": \"DRV-SMOKE\",
      \"timestamp\": \"${TIMESTAMP}\",
      \"latitude\": 4.7110,
      \"longitude\": -74.0721,
      \"speedKmh\": 42.5,
      \"fuelLevelPercent\": 77,
      \"batteryPercent\": 88
    }" 2>/dev/null || echo "000")

  if [ "$code" = "202" ]; then
    SEND_OK="OK"
    ok "Evento válido enviado"
    return 0
  fi
  fail "Evento válido enviado" "HTTP ${code}"
  return 1
}

wait_processed() {
  step "Esperando procesamiento del Worker (${WAIT_SECONDS}s) y consultando flota"
  local i
  for i in $(seq 1 "$WAIT_SECONDS"); do
    sleep 1
    if curl -fsS --max-time 5 "${API_BASE}/api/fleet/${DEVICE_ID}" 2>/dev/null | grep -qi "${DEVICE_ID}"; then
      PROCESS_OK="OK"
      ok "Evento procesado"
      return 0
    fi
  done
  fail "Evento procesado" "dispositivo no encontrado en /api/fleet/${DEVICE_ID}"
  return 1
}

test_dlq() {
  local marker="SMOKE-DLQ-${RANDOM}${RANDOM}"
  local payload="{\"deviceId\":\"${marker}\"}"
  local tmp
  tmp="$(mktemp 2>/dev/null || echo /tmp/fleet-smoke-dlq.$$)"

  step "Suscribiendo a telemetry.dead-letter y publicando payload inválido (marker=${marker})"
  docker exec fleet-redpanda sh -c \
    "timeout ${DLQ_TIMEOUT}s rpk topic consume telemetry.dead-letter --brokers localhost:9092 -n 1 -o end -f '%v\n'" \
    >"$tmp" 2>/dev/null &
  local consumer_pid=$!
  sleep 2

  if ! printf '%s\n' "$payload" | docker exec -i fleet-redpanda rpk topic produce telemetry.raw --brokers localhost:9092 >/dev/null 2>&1; then
    kill "$consumer_pid" 2>/dev/null || true
    wait "$consumer_pid" 2>/dev/null || true
    rm -f "$tmp"
    fail "DLQ validada" "no se pudo producir a telemetry.raw"
    return 1
  fi

  echo "Esperando Worker → DLQ (hasta ${DLQ_TIMEOUT}s)..."
  wait "$consumer_pid" 2>/dev/null || true
  local output
  output="$(cat "$tmp" 2>/dev/null || true)"
  rm -f "$tmp"

  if echo "$output" | grep -Eq 'invalid_(payload|domain|json)' && echo "$output" | grep -q "$marker"; then
    DLQ_OK="OK"
    ok "DLQ validada"
    return 0
  fi

  # Fallback acotado
  sleep "$WAIT_SECONDS"
  output=$(docker exec fleet-redpanda sh -c \
    "timeout 8s rpk topic consume telemetry.dead-letter --brokers localhost:9092 -n 50 -f '%v\n'" 2>/dev/null || true)

  if echo "$output" | grep -Eq 'invalid_(payload|domain|json)' && echo "$output" | grep -q "$marker"; then
    DLQ_OK="OK"
    ok "DLQ validada"
    return 0
  fi

  fail "DLQ validada" "no se encontró invalid_domain/invalid_payload/invalid_json para el marker"
  echo "Salida DLQ (recorte): $(printf '%s' "$output" | head -c 400)"
  return 1
}

printf 'Fleet Telemetry — smoke test E2E\n'
printf 'API: %s\n' "$API_BASE"

if check_api; then
  if send_valid; then
    wait_processed || true
  fi
  test_dlq || true
fi

printf '\n========== RESUMEN ==========\n'
printf 'API disponible: %s\n' "$API_OK"
printf 'Evento válido enviado: %s\n' "$SEND_OK"
printf 'Evento procesado: %s\n' "$PROCESS_OK"
printf 'DLQ validada: %s\n' "$DLQ_OK"

if [ "$API_OK" = "OK" ] && [ "$SEND_OK" = "OK" ] && [ "$PROCESS_OK" = "OK" ] && [ "$DLQ_OK" = "OK" ]; then
  printf 'Resultado final: PASSED\n'
  printf '==============================\n'
  exit 0
fi

printf 'Resultado final: FAILED\n'
printf '==============================\n'
exit 1
