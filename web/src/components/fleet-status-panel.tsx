/** Panel lateral con lista de vehículos y su estado. */
import {
  Bike,
  Bus,
  Car,
  CarFront,
  CarTaxiFront,
  Navigation,
  Truck,
  type LucideIcon,
} from "lucide-react";
import type { AggregationSource } from "@/lib/analytics";
import type { VehicleStatus, VehicleType } from "@/types/fleet";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { esVehiculoEnLinea } from "@/lib/labels";
import { formatFleetStatusCard } from "@/lib/vehicle-display-format";
import { VehicleConnectivityBadge } from "@/components/vehicle-connectivity-badge";
import { vehicleTypeLabel } from "@/lib/vehicle-types";
import { cn } from "@/lib/utils";

const VEHICLE_TYPE_ICONS: Record<VehicleType, LucideIcon> = {
  car: Car,
  motorcycle: Bike,
  van: CarFront,
  truck: Truck,
  bus: Bus,
  pickup: CarTaxiFront,
};

const VEHICLE_TYPE_ICON_TONES: Record<VehicleType, { online: string; offline: string; inkOnline: string }> = {
  car: { online: "bg-emerald-500/15", offline: "bg-slate-200/60", inkOnline: "text-emerald-600" },
  motorcycle: { online: "bg-amber-500/15", offline: "bg-slate-200/60", inkOnline: "text-amber-600" },
  van: { online: "bg-teal-500/15", offline: "bg-slate-200/60", inkOnline: "text-teal-600" },
  truck: { online: "bg-blue-500/15", offline: "bg-slate-200/60", inkOnline: "text-blue-600" },
  bus: { online: "bg-violet-500/15", offline: "bg-slate-200/60", inkOnline: "text-violet-600" },
  pickup: { online: "bg-red-500/15", offline: "bg-slate-200/60", inkOnline: "text-red-600" },
};

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

/** Icono Lucide del tipo: siluetas reconocibles (moto ≠ camión ≠ auto). */
function VehicleTypeIcon({ type, online }: { type: VehicleType; online: boolean }) {
  const Icon = VEHICLE_TYPE_ICONS[type] ?? Car;
  const tones = VEHICLE_TYPE_ICON_TONES[type] ?? VEHICLE_TYPE_ICON_TONES.car;
  return (
    <Icon
      className={cn("h-5 w-5", online ? tones.inkOnline : "text-slate-400")}
      aria-hidden="true"
      strokeWidth={2.25}
    />
  );
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
            const typeLabel = vehicleTypeLabel(vehicle.vehicleType);
            const tones = VEHICLE_TYPE_ICON_TONES[vehicle.vehicleType] ?? VEHICLE_TYPE_ICON_TONES.car;
            const card = formatFleetStatusCard(vehicle);

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
                  "group flex w-full items-start gap-3 rounded-xl border p-3.5 text-left transition-all duration-200",
                  selected
                    ? "border-primary/40 bg-primary/5 shadow-glow ring-1 ring-primary/20"
                    : "border-border bg-slate-50 hover:border-slate-300 hover:bg-white hover:shadow-soft",
                )}
              >
                <div
                  className={cn(
                    "relative flex h-10 w-10 shrink-0 items-center justify-center rounded-xl",
                    online ? tones.online : tones.offline,
                  )}
                  aria-label={`Tipo de vehículo: ${typeLabel}`}
                >
                  <span
                    className={cn(
                      "absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full border-2 border-white",
                      online ? "bg-emerald-500 animate-pulse-soft" : "bg-slate-400",
                    )}
                  />
                  <VehicleTypeIcon type={vehicle.vehicleType} online={online} />
                </div>

                <div className="min-w-0 flex-1 space-y-0.5">
                  <div className="flex min-w-0 items-center gap-2">
                    <p className="truncate font-semibold text-slate-800">{card.name}</p>
                    <VehicleConnectivityBadge status={card.status} />
                  </div>
                  <p
                    className="break-all font-mono text-[11px] text-slate-500"
                    title={card.deviceId}
                  >
                    {card.deviceId}
                  </p>
                  <p className="text-xs text-slate-500">{card.metrics}</p>
                </div>
              </button>
            );
          })}
        </div>
      </CardContent>
    </Card>
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
