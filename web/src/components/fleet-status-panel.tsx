import type { VehicleStatus } from "@/types/fleet";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";

export function FleetStatusPanel({ vehicles }: { vehicles: VehicleStatus[] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Estado de flota</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="space-y-3">
          {vehicles.length === 0 && (
            <p className="text-sm text-muted-foreground">Sin vehículos con telemetría.</p>
          )}
          {vehicles.map((vehicle) => (
            <div key={vehicle.vehicleId} className="flex items-center justify-between rounded-md border border-border p-3">
              <div>
                <p className="font-medium">{vehicle.vehicleId}</p>
                <p className="text-xs text-muted-foreground">
                  {vehicle.lastSpeedKmh?.toFixed(1) ?? "—"} km/h ·{" "}
                  {vehicle.lastSeenAt ? new Date(vehicle.lastSeenAt).toLocaleTimeString() : "—"}
                </p>
              </div>
              <Badge variant={vehicle.status === "online" ? "success" : "outline"}>{vehicle.status}</Badge>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}
