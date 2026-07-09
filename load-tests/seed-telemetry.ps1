# Genera y envía telemetría masiva a la API sin requerir k6.
# Vehículos dispersos en zonas de Bogotá con mezcla online/offline.
# Uso:
#   .\seed-telemetry.ps1
#   .\seed-telemetry.ps1 -TotalEvents 5000 -BatchSize 100 -VehicleCount 500

param(
    [string]$ApiUrl = "http://localhost:5000",
    [int]$TotalEvents = 3000,
    [int]$BatchSize = 50,
    [int]$VehicleCount = 500,
    [string]$AuthToken = ""
)

$ErrorActionPreference = "Stop"

$Zones = @(
    @{ Lat = 4.648; Lng = -74.063; Spread = 0.018; Name = "Chapinero" },
    @{ Lat = 4.711; Lng = -74.032; Spread = 0.015; Name = "Usaquen" },
    @{ Lat = 4.737; Lng = -74.082; Spread = 0.020; Name = "Suba" },
    @{ Lat = 4.628; Lng = -74.152; Spread = 0.022; Name = "Kennedy" },
    @{ Lat = 4.598; Lng = -74.075; Spread = 0.012; Name = "Centro" },
    @{ Lat = 4.702; Lng = -74.108; Spread = 0.016; Name = "Engativa" },
    @{ Lat = 4.628; Lng = -74.090; Spread = 0.014; Name = "Teusaquillo" },
    @{ Lat = 4.669; Lng = -74.145; Spread = 0.015; Name = "Fontibon" },
    @{ Lat = 4.568; Lng = -74.085; Spread = 0.018; Name = "San Cristobal" },
    @{ Lat = 4.612; Lng = -74.195; Spread = 0.020; Name = "Bosa" }
)

function Get-RandomPointInZone {
    param($Zone)
    $angle = (Get-Random -Minimum 0 -Maximum 6283) / 1000.0
    $radius = (Get-Random -Minimum 0 -Maximum 1000) / 1000.0 * $Zone.Spread
    $lat = [math]::Round(($Zone.Lat + [math]::Cos($angle) * $radius), 5)
    $lng = [math]::Round(($Zone.Lng + [math]::Sin($angle) * $radius), 5)
    return @{ Lat = $lat; Lng = $lng }
}

function Get-RandomTimestamp {
    param([bool]$Online)
    if ($Online) {
        $secondsAgo = Get-Random -Minimum 0 -Maximum 240
        return (Get-Date).ToUniversalTime().AddSeconds(-$secondsAgo).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    }
    $minutesAgo = Get-Random -Minimum 8 -Maximum 55
    return (Get-Date).ToUniversalTime().AddMinutes(-$minutesAgo).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
}

function New-TelemetryEvent {
    param([int]$VehicleIndex)
    $vehicleNum = Get-Random -Minimum 1 -Maximum ($VehicleCount + 1)
    $vehicleId = "VH-{0:D3}" -f $vehicleNum
    $zone = $Zones[$vehicleNum % $Zones.Count]
    $point = Get-RandomPointInZone -Zone $zone
    $online = (Get-Random -Minimum 1 -Maximum 101) -le 62

    return @{
        eventId = [guid]::NewGuid().ToString()
        vehicleId = $vehicleId
        driverId = "DRV-$($vehicleNum.ToString('D3'))"
        timestamp = Get-RandomTimestamp -Online $online
        latitude = $point.Lat
        longitude = $point.Lng
        speedKmh = if ($online) { Get-Random -Minimum 15 -Maximum 130 } else { Get-Random -Minimum 0 -Maximum 12 }
        fuelLevelPercent = Get-Random -Minimum 5 -Maximum 95
        batteryPercent = Get-Random -Minimum 25 -Maximum 100
    }
}

$headers = @{ "Content-Type" = "application/json" }
if ($AuthToken) {
    $headers["Authorization"] = "Bearer $AuthToken"
}

Write-Host "Enviando $TotalEvents eventos a $ApiUrl (lotes de $BatchSize, $VehicleCount vehiculos, zonas Bogota)..."

$sent = 0
$failed = 0
$batch = @()

for ($i = 1; $i -le $TotalEvents; $i++) {
    $batch += New-TelemetryEvent -VehicleIndex $i

    if ($batch.Count -ge $BatchSize -or $i -eq $TotalEvents) {
        $body = @{ events = $batch } | ConvertTo-Json -Depth 6 -Compress

        try {
            Invoke-RestMethod -Uri "$ApiUrl/api/telemetry/batch" -Method Post -Headers $headers -Body $body -TimeoutSec 60 | Out-Null
            $sent += $batch.Count
            Write-Host "  OK: $sent / $TotalEvents"
        }
        catch {
            $failed += $batch.Count
            Write-Warning "  Lote fallido ($($batch.Count) eventos): $($_.Exception.Message)"
        }

        $batch = @()
        Start-Sleep -Milliseconds 50
    }
}

Write-Host ""
Write-Host "Listo. Enviados: $sent | Fallidos: $failed"
Write-Host "Verifica: $ApiUrl/api/fleet"
