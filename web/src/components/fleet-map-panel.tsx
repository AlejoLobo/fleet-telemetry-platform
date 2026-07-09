import type { VehicleStatus } from "@/types/fleet";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function FleetMapPanel({ vehicles }: { vehicles: VehicleStatus[] }) {
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>Coordenadas de vehículos</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="relative h-64 overflow-hidden rounded-md border border-border bg-slate-950">
          <div className="absolute inset-0 opacity-20" style={{
            backgroundImage: "linear-gradient(#334155 1px, transparent 1px), linear-gradient(90deg, #334155 1px, transparent 1px)",
            backgroundSize: "24px 24px",
          }} />
          {vehicles.map((vehicle) => {
            if (vehicle.lastLatitude == null || vehicle.lastLongitude == null) return null;
            const x = ((vehicle.lastLongitude + 74.12) / 0.08) * 100;
            const y = ((4.72 - vehicle.lastLatitude) / 0.08) * 100;
            return (
              <div
                key={vehicle.vehicleId}
                className="absolute -translate-x-1/2 -translate-y-1/2"
                style={{ left: `${Math.min(Math.max(x, 5), 95)}%`, top: `${Math.min(Math.max(y, 5), 95)}%` }}
                title={`${vehicle.vehicleId}: ${vehicle.lastLatitude}, ${vehicle.lastLongitude}`}
              >
                <span className={`block h-3 w-3 rounded-full ${vehicle.status === "online" ? "bg-emerald-400" : "bg-slate-400"}`} />
                <span className="mt-1 block text-[10px] font-medium text-white">{vehicle.vehicleId}</span>
              </div>
            );
          })}
        </div>
        <p className="mt-2 text-xs text-muted-foreground">
          Vista simplificada (Bogotá). En producción: mapa interactivo.
        </p>
      </CardContent>
    </Card>
  );
}
