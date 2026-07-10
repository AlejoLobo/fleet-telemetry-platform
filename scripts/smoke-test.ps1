#!/usr/bin/env pwsh
#Requires -Version 5.1
<#
.SYNOPSIS
  Smoke test end-to-end de la demo local (API → Kafka → Worker → TimescaleDB → consulta + DLQ).

.NOTES
  Requiere el stack levantado:
    docker compose --profile app up -d --build
#>

$ErrorActionPreference = "Continue"
$ApiBase = if ($env:FLEET_API_URL) { $env:FLEET_API_URL.TrimEnd("/") } else { "http://localhost:5000" }
$WaitSeconds = 6
$DlqTimeoutSeconds = 12

$results = [ordered]@{
    "API disponible"         = "FAIL"
    "Evento válido enviado"  = "FAIL"
    "Evento procesado"       = "FAIL"
    "DLQ validada"           = "FAIL"
}

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Test-ApiAlive {
    Write-Step "Verificando API en $ApiBase/health/live"
    try {
        $response = Invoke-WebRequest -Uri "$ApiBase/health/live" -Method GET -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200 -and $response.Content -match "alive") {
            $results["API disponible"] = "OK"
            Write-Host "API disponible: OK" -ForegroundColor Green
            return $true
        }
    }
    catch {
        Write-Host "API disponible: FAIL ($($_.Exception.Message))" -ForegroundColor Red
    }
    return $false
}

function Send-ValidTelemetry {
    $eventId = [guid]::NewGuid().ToString()
    $vehicleId = "SMOKE-$([guid]::NewGuid().ToString('N').Substring(0, 8).ToUpperInvariant())"
    $timestamp = [DateTimeOffset]::UtcNow.ToString("o")

    $body = @{
        eventId          = $eventId
        vehicleId        = $vehicleId
        driverId         = "DRV-SMOKE"
        timestamp        = $timestamp
        latitude         = 4.7110
        longitude        = -74.0721
        speedKmh         = 42.5
        fuelLevelPercent = 77
        batteryPercent   = 88
    } | ConvertTo-Json

    Write-Step "Enviando evento válido vehicleId=$vehicleId"
    try {
        $response = Invoke-WebRequest -Uri "$ApiBase/api/telemetry" -Method POST -Body $body -ContentType "application/json" -UseBasicParsing -TimeoutSec 10
        if ($response.StatusCode -eq 202) {
            $results["Evento válido enviado"] = "OK"
            Write-Host "Evento válido enviado: OK (202)" -ForegroundColor Green
            return @{ EventId = $eventId; VehicleId = $vehicleId }
        }
        Write-Host "Evento válido enviado: FAIL (HTTP $($response.StatusCode))" -ForegroundColor Red
    }
    catch {
        Write-Host "Evento válido enviado: FAIL ($($_.Exception.Message))" -ForegroundColor Red
    }
    return $null
}

function Wait-ForProcessed([string]$VehicleId) {
    Write-Step "Esperando procesamiento del Worker (${WaitSeconds}s) y consultando flota"
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        Start-Sleep -Seconds 1
        try {
            $response = Invoke-RestMethod -Uri "$ApiBase/api/fleet/$VehicleId" -Method GET -TimeoutSec 5
            if ($null -ne $response -and $response.vehicleId -eq $VehicleId) {
                $results["Evento procesado"] = "OK"
                Write-Host "Evento procesado: OK (vehicleId=$VehicleId)" -ForegroundColor Green
                return $true
            }
        }
        catch {
            # 404 mientras el Worker aún no persiste
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    Write-Host "Evento procesado: FAIL (vehículo no encontrado en /api/fleet/$VehicleId)" -ForegroundColor Red
    return $false
}

function Test-DeadLetterQueue {
    $marker = "SMOKE-DLQ-$([guid]::NewGuid().ToString('N').Substring(0, 12))"
    $invalidPayload = "{`"vehicleId`":`"$marker`"}"

    Write-Step "Suscribiendo a telemetry.dead-letter y publicando payload inválido (marker=$marker)"

    $job = Start-Job -ScriptBlock {
        param($TimeoutSec)
        docker exec fleet-redpanda sh -c "timeout ${TimeoutSec}s rpk topic consume telemetry.dead-letter --brokers localhost:9092 -n 1 -o end -f '%v\n'" 2>&1
    } -ArgumentList $DlqTimeoutSeconds

    Start-Sleep -Seconds 2

    try {
        $produceOut = $invalidPayload | docker exec -i fleet-redpanda rpk topic produce telemetry.raw --brokers localhost:9092 2>&1
        if ($LASTEXITCODE -ne 0) {
            Stop-Job $job -ErrorAction SilentlyContinue
            Remove-Job $job -Force -ErrorAction SilentlyContinue
            Write-Host "DLQ validada: FAIL (no se pudo producir a telemetry.raw): $produceOut" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Stop-Job $job -ErrorAction SilentlyContinue
        Remove-Job $job -Force -ErrorAction SilentlyContinue
        Write-Host "DLQ validada: FAIL ($($_.Exception.Message))" -ForegroundColor Red
        return $false
    }

    Write-Host "Esperando Worker → DLQ (hasta ${DlqTimeoutSeconds}s)..."
    $null = Wait-Job $job -Timeout ($DlqTimeoutSeconds + 3)
    $scan = ""
    if ($job.State -eq "Completed" -or $job.State -eq "Failed") {
        $scan = (Receive-Job $job | Out-String)
    }
    else {
        Stop-Job $job -ErrorAction SilentlyContinue
    }
    Remove-Job $job -Force -ErrorAction SilentlyContinue

    if (-not ($scan -match [regex]::Escape($marker) -and $scan -match "invalid_payload")) {
        # Fallback acotado: escanear mensajes recientes
        Start-Sleep -Seconds $WaitSeconds
        $scan = docker exec fleet-redpanda sh -c "timeout 8s rpk topic consume telemetry.dead-letter --brokers localhost:9092 -n 50 -f '%v\n'" 2>&1 | Out-String
    }

    if ($scan -match [regex]::Escape($marker) -and $scan -match "invalid_payload") {
        $results["DLQ validada"] = "OK"
        Write-Host "DLQ validada: OK (reason=invalid_payload)" -ForegroundColor Green
        return $true
    }

    Write-Host "DLQ validada: FAIL (no se encontró invalid_payload para el marker)" -ForegroundColor Red
    $previewLen = [Math]::Min(400, $scan.Length)
    if ($previewLen -gt 0) {
        Write-Host "Salida DLQ (recorte): $($scan.Substring(0, $previewLen))"
    }
    return $false
}

# --- main ---
Write-Host "Fleet Telemetry — smoke test E2E" -ForegroundColor White
Write-Host "API: $ApiBase"

$apiOk = Test-ApiAlive
$sent = $null
$processedOk = $false
$dlqOk = $false

if ($apiOk) {
    $sent = Send-ValidTelemetry
    if ($null -ne $sent) {
        $processedOk = Wait-ForProcessed -VehicleId $sent.VehicleId
    }
    $dlqOk = Test-DeadLetterQueue
}

Write-Host "`n========== RESUMEN ==========" -ForegroundColor White
foreach ($key in $results.Keys) {
    $color = if ($results[$key] -eq "OK") { "Green" } else { "Red" }
    Write-Host ("{0}: {1}" -f $key, $results[$key]) -ForegroundColor $color
}

$passed = ($results.Values | Where-Object { $_ -ne "OK" }).Count -eq 0
$final = if ($passed) { "PASSED" } else { "FAILED" }
Write-Host ("Resultado final: {0}" -f $final) -ForegroundColor $(if ($passed) { "Green" } else { "Red" })
Write-Host "==============================`n"

if (-not $passed) { exit 1 }
exit 0
