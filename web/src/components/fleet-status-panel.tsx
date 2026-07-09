import { Navigation, Clock, Gauge } from "lucide-react";
import type { VehicleStatus } from "@/types/fleet";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { esVehiculoEnLinea, etiquetaEstadoVehiculo } from "@/lib/labels";
import { cn } from "@/lib/utils";

type FleetStatusPanelProps = {
  vehicles: VehicleStatus[];
  selectedVehicleId?: string;
  onSelectVehicle?: (vehicleId: string) => void;
};

export function FleetStatusPanel({
  vehicles,
  selectedVehicleId,
  onSelectVehicle,
}: FleetStatusPanelProps) {
  const onlineCount = vehicles.filter((v) => esVehiculoEnLinea(v.status)).length;

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
              {onlineCount} en línea · {vehicles.length} total
            </CardDescription>
          </div>
          <Badge variant="success" className="tabular-nums">
            {onlineCount}/{vehicles.length}
          </Badge>
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
            const selected = vehicle.vehicleId === selectedVehicleId;
            const online = esVehiculoEnLinea(vehicle.status);
            const speed = vehicle.lastSpeedKmh ?? 0;

            return (
              <button
                key={vehicle.vehicleId}
                type="button"
                onClick={() => onSelectVehicle?.(vehicle.vehicleId)}
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
                    <p className="truncate font-semibold text-slate-800">{vehicle.vehicleId}</p>
                    <Badge variant={online ? "success" : "outline"} className="shrink-0 text-[10px]">
                      {etiquetaEstadoVehiculo(vehicle.status)}
                    </Badge>
                  </div>
                  {vehicle.name && (
                    <p className="truncate text-xs text-slate-500">{vehicle.name}</p>
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

function TruckPlaceholder() {
  return (
    <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
      <Navigation className="h-7 w-7 text-slate-300" />
    </div>
  );
}
