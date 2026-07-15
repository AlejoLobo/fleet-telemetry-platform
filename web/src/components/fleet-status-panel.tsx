/** Panel lateral con lista de vehículos y su estado. */
import { Navigation, Clock, Gauge } from "lucide-react";
import type { AggregationSource } from "@/lib/analytics";
import type { VehicleStatus } from "@/types/fleet";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { esVehiculoEnLinea, etiquetaEstadoVehiculo, formatVehicleDisplayName } from "@/lib/labels";
import { cn } from "@/lib/utils";

type FleetStatusPanelProps = {
  vehicles: VehicleStatus[];
  selectedDeviceId?: string | null;
  fleetTruncated?: boolean;
  aggregationSource?: AggregationSource;
  totalVehiclesGlobal?: number;
  activeVehiclesGlobal?: number;
  onSelectVehicle?: (deviceId: string) => void;
  onFocusVehicle?: (deviceId: string) => void;
};

function formatCount(value: number): string {
  return value.toLocaleString("es-CO");
}

/** Lista interactiva de vehículos con velocidad y estado. */
export function FleetStatusPanel({
  vehicles,
  selectedDeviceId,
  fleetTruncated = false,
  aggregationSource = "snapshot",
  totalVehiclesGlobal,
  activeVehiclesGlobal,
  onSelectVehicle,
  onFocusVehicle,
}: FleetStatusPanelProps) {
  const onlineInSnapshot = vehicles.filter((v) => esVehiculoEnLinea(v.status)).length;
  const shownCount = vehicles.length;
  const useOpsGlobals =
    fleetTruncated && aggregationSource === "ops" && totalVehiclesGlobal != null;

  let description: string;
  let badgeValue: string;
  let badgeHint: string | null = null;

  if (useOpsGlobals) {
    description = `${formatCount(shownCount)} mostrados de ${formatCount(totalVehiclesGlobal!)} · ${formatCount(onlineInSnapshot)} en línea dentro del snapshot mostrado`;
    badgeValue = `${formatCount(activeVehiclesGlobal ?? onlineInSnapshot)}/${formatCount(totalVehiclesGlobal!)}`;
    badgeHint = "agregados globales";
  } else if (fleetTruncated) {
    description = `${formatCount(shownCount)} vehículos mostrados · total global no disponible · ${formatCount(onlineInSnapshot)} en línea en snapshot`;
    badgeValue = `${onlineInSnapshot}/${shownCount}`;
    badgeHint = "métricas parciales del snapshot";
  } else {
    description = `${onlineInSnapshot} en línea · ${shownCount} total`;
    badgeValue = `${onlineInSnapshot}/${shownCount}`;
    badgeHint = null;
  }

  return (
    <Card className="flex h-full flex-col">
      <CardHeader className="border-b border-border">
        <div className="flex items-center justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Navigation className="h-4 w-4 text-emerald-500" />
              Estado de flota
            </CardTitle>
            <CardDescription className="mt-1">
              {description} · doble clic centra en mapa
            </CardDescription>
          </div>
          <div className="text-right">
            <Badge variant="success" className="tabular-nums">
              {badgeValue}
            </Badge>
            {badgeHint && (
              <p className="mt-1 text-[10px] text-muted-foreground">{badgeHint}</p>
            )}
          </div>
        </div>
      </CardHeader>
      <CardContent className="custom-scrollbar max-h-[420px] flex-1 overflow-y-auto pt-4">
        <div className="space-y-2">
          {vehicles.length === 0 && (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <TruckPlaceholder />
              <p className="mt-3 text-sm font-medium text-slate-600">Sin vehículos</p>
              <p className="text-xs text-muted-foreground">Carga datos desde API o modo demo</p>
            </div>
          )}
          {vehicles.map((vehicle) => {
            const selected = vehicle.deviceId === selectedDeviceId;
            const online = esVehiculoEnLinea(vehicle.status);
            const speed = vehicle.lastSpeedKmh ?? 0;
            const displayName = formatVehicleDisplayName(vehicle);

            return (
              <button
                key={vehicle.deviceId}
                type="button"
                onClick={() => onSelectVehicle?.(vehicle.deviceId)}
                onDoubleClick={(event) => {
                  event.preventDefault();
                  onFocusVehicle?.(vehicle.deviceId);
                }}
                className={cn(
                  "group flex w-full items-center gap-3 rounded-xl border p-3.5 text-left transition-all duration-200",
                  selected
                    ? "border-primary/40 bg-primary/5 shadow-glow ring-1 ring-primary/20"
                    : "border-border bg-slate-50 hover:border-slate-300 hover:bg-white hover:shadow-soft",
                )}
              >
                <div
                  className={cn(
                    "relative flex h-10 w-10 shrink-0 items-center justify-center rounded-xl",
                    online ? "bg-emerald-500/10" : "bg-slate-200/60",
                  )}
                >
                  <span
                    className={cn(
                      "absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full border-2 border-white",
                      online ? "bg-emerald-500 animate-pulse-soft" : "bg-slate-400",
                    )}
                  />
                  <Gauge
                    className={cn("h-4 w-4", online ? "text-emerald-600" : "text-slate-400")}
                  />
                </div>

                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate font-semibold text-slate-800">{displayName}</p>
                    <Badge variant={online ? "success" : "outline"} className="shrink-0 text-[10px]">
                      {etiquetaEstadoVehiculo(vehicle.status)}
                    </Badge>
                    {vehicle.lastLocationSource === "simulated" && (
                      <Badge variant="outline" className="shrink-0 text-[10px]">Simulado</Badge>
                    )}
                  </div>
                  {vehicle.vehicleName && vehicle.vehicleName !== displayName && (
                    <p className="truncate text-xs text-slate-500">{vehicle.vehicleName}</p>
                  )}
                  <div className="mt-1 flex items-center gap-3 text-xs text-slate-500">
                    <span className="flex items-center gap-1">
                      <Gauge className="h-3 w-3" />
                      {speed.toFixed(0)} km/h
                    </span>
                    <span className="flex items-center gap-1">
                      <Clock className="h-3 w-3" />
                      {vehicle.lastSeenAt
                        ? new Date(vehicle.lastSeenAt).toLocaleTimeString("es-CO", {
                            hour: "2-digit",
                            minute: "2-digit",
                          })
                        : "—"}
                    </span>
                  </div>
                </div>

                <div className="hidden shrink-0 sm:block">
                  <SpeedRing speed={speed} online={online} />
                </div>
              </button>
            );
          })}
        </div>
      </CardContent>
    </Card>
  );
}

/** Anillo circular que muestra velocidad relativa. */
function SpeedRing({ speed, online }: { speed: number; online: boolean }) {
  const max = 120;
  const pct = Math.min(speed / max, 1);
  const circumference = 2 * Math.PI * 14;
  const offset = circumference * (1 - pct);

  return (
    <svg width="36" height="36" className="-rotate-90">
      <circle cx="18" cy="18" r="14" fill="none" stroke="#e2e8f0" strokeWidth="3" />
      <circle
        cx="18"
        cy="18"
        r="14"
        fill="none"
        stroke={online ? "#10b981" : "#94a3b8"}
        strokeWidth="3"
        strokeDasharray={circumference}
        strokeDashoffset={offset}
        strokeLinecap="round"
        className="transition-all duration-500"
      />
    </svg>
  );
}

/** Icono vacío cuando no hay vehículos cargados. */
function TruckPlaceholder() {
  return (
    <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
      <Navigation className="h-7 w-7 text-slate-300" />
    </div>
  );
}
