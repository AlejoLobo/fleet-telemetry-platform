/** Tabla de eventos de telemetría del vehículo seleccionado. */
import { Battery, Fuel, Gauge, MapPin, TableProperties } from "lucide-react";
import type { TelemetryEvent } from "@/types/fleet";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

/** Muestra historial de velocidad, combustible y ubicación. */
export function TelemetryTable({ events, vehicleId }: { events: TelemetryEvent[]; vehicleId: string }) {
  return (
    <Card className="h-full">
      <CardHeader className="border-b border-border">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <CardTitle className="flex items-center gap-2">
              <TableProperties className="h-4 w-4 text-primary" />
              Telemetría detallada
            </CardTitle>
            <CardDescription className="mt-1">
              Historial de eventos · vehículo <span className="font-medium text-slate-700">{vehicleId}</span>
            </CardDescription>
          </div>
          <Badge variant="outline">{events.length} eventos</Badge>
        </div>
      </CardHeader>
      <CardContent className="p-0">
        <div className="custom-scrollbar max-h-[360px] overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 z-10 bg-slate-50/95 backdrop-blur-sm">
              <tr className="border-b border-border text-left text-xs font-semibold uppercase tracking-wider text-slate-600">
                <th className="px-5 py-3">Fecha y hora</th>
                <th className="px-4 py-3">
                  <span className="flex items-center gap-1">
                    <Gauge className="h-3.5 w-3.5" /> Velocidad
                  </span>
                </th>
                <th className="px-4 py-3">
                  <span className="flex items-center gap-1">
                    <Fuel className="h-3.5 w-3.5" /> Combustible
                  </span>
                </th>
                <th className="px-4 py-3">
                  <span className="flex items-center gap-1">
                    <Battery className="h-3.5 w-3.5" /> Batería
                  </span>
                </th>
                <th className="px-5 py-3">
                  <span className="flex items-center gap-1">
                    <MapPin className="h-3.5 w-3.5" /> Ubicación
                  </span>
                </th>
              </tr>
            </thead>
            <tbody>
              {events.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-5 py-16 text-center">
                    <Gauge className="mx-auto h-8 w-8 text-slate-200" />
                    <p className="mt-2 text-sm text-muted-foreground">
                      Sin eventos en las últimas 24 h
                    </p>
                  </td>
                </tr>
              )}
              {events.map((event, i) => (
                <tr
                  key={event.eventId}
                  className={cn(
                    "border-b border-slate-200/70 transition-colors hover:bg-sky-50/60",
                    i % 2 === 0 ? "bg-white" : "bg-slate-50",
                  )}
                >
                  <td className="whitespace-nowrap px-5 py-3 font-medium text-slate-700">
                    {new Date(event.timestamp).toLocaleString("es-CO")}
                  </td>
                  <td className="px-4 py-3">
                    <span
                      className={cn(
                        "inline-flex rounded-lg px-2 py-0.5 text-xs font-semibold",
                        event.speedKmh > 80
                          ? "bg-red-50 text-red-600"
                          : event.speedKmh > 50
                            ? "bg-amber-50 text-amber-600"
                            : "bg-emerald-50 text-emerald-600",
                      )}
                    >
                      {event.speedKmh.toFixed(1)} km/h
                    </span>
                  </td>
                  <td className="px-4 py-3 text-slate-600">
                    {event.fuelLevelPercent != null ? (
                      <FuelBar value={event.fuelLevelPercent} type="fuel" />
                    ) : (
                      "—"
                    )}
                  </td>
                  <td className="px-4 py-3 text-slate-600">
                    {event.batteryPercent != null ? (
                      <FuelBar value={event.batteryPercent} type="battery" />
                    ) : (
                      "—"
                    )}
                  </td>
                  <td className="px-5 py-3 font-mono text-xs text-slate-500">
                    {event.latitude.toFixed(4)}, {event.longitude.toFixed(4)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}

/** Barra de progreso para combustible o batería. */
function FuelBar({ value, type }: { value: number; type: "fuel" | "battery" }) {
  const low = value < 25;
  const mid = value < 50;

  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-16 overflow-hidden rounded-full bg-slate-100">
        <div
          className={cn(
            "h-full rounded-full transition-all",
            low ? "bg-red-400" : mid ? "bg-amber-400" : type === "fuel" ? "bg-sky-400" : "bg-emerald-400",
          )}
          style={{ width: `${Math.min(value, 100)}%` }}
        />
      </div>
      <span className="text-xs tabular-nums">{value.toFixed(0)}%</span>
    </div>
  );
}
