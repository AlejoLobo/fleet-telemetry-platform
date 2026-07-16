/** Badge de conectividad: En línea (verde) / Desconectado (gris). */
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { VehicleStatusBadgeInfo } from "@/lib/vehicle-display-format";

type VehicleConnectivityBadgeProps = {
  status: VehicleStatusBadgeInfo;
  className?: string;
};

export function VehicleConnectivityBadge({ status, className }: VehicleConnectivityBadgeProps) {
  return (
    <Badge
      variant={status.online ? "success" : "outline"}
      className={cn(
        "shrink-0 text-[10px]",
        !status.online && "border-slate-300 bg-slate-100 text-slate-600",
        className,
      )}
    >
      {status.label}
    </Badge>
  );
}
