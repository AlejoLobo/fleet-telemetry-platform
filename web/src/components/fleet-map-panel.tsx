/** Panel contenedor del mapa Leaflet con estadísticas. */
"use client";

import dynamic from "next/dynamic";
import { Layers, MapPin, Maximize2 } from "lucide-react";
import type { VehicleStatus } from "@/types/fleet";
import type { MapFocusTarget } from "@/components/maps/leaflet-fleet-map";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { esVehiculoEnLinea } from "@/lib/labels";

// Carga dinámica del mapa (sin SSR por Leaflet)
const LeafletFleetMap = dynamic(
  () => import("@/components/maps/leaflet-fleet-map").then((m) => m.LeafletFleetMap),
  {
    ssr: false,
    loading: () => (
      <div className="flex h-full min-h-[400px] flex-col items-center justify-center gap-3 rounded-xl bg-gradient-to-br from-sky-50 to-violet-50">
        <div className="h-10 w-10 animate-spin rounded-full border-2 border-primary border-t-transparent" />
        <p className="text-sm text-slate-500">Cargando mapa…</p>
      </div>
    ),
  },
);

type FleetMapPanelProps = {
  vehicles: VehicleStatus[];
  selectedDeviceId?: string | null;
  focusTarget?: MapFocusTarget | null;
  autoFit?: boolean;
};

/** Tarjeta del mapa en tiempo real con badges de estado. */
export function FleetMapPanel({
  vehicles,
  selectedDeviceId,
  focusTarget,
  autoFit = true,
}: FleetMapPanelProps) {
  const withCoords = vehicles.filter(
    (v) => v.lastLatitude != null && v.lastLongitude != null,
  );
  const onlineOnMap = withCoords.filter((v) => esVehiculoEnLinea(v.status)).length;

  return (
    <Card className="overflow-hidden">
      <CardHeader className="border-b border-border bg-gradient-to-r from-sky-50 via-white to-violet-50">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2 text-lg">
              <MapPin className="h-5 w-5 text-primary" />
              Mapa en tiempo real
            </CardTitle>
            <CardDescription className="mt-1 flex items-center gap-2">
              <Layers className="h-3.5 w-3.5" />
              OpenStreetMap · Ajuste a calles OSRM
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="success" className="gap-1.5">
              <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
              {onlineOnMap} en línea
            </Badge>
            <Badge variant="outline" className="gap-1.5">
              <span className="h-1.5 w-1.5 rounded-full bg-slate-400" />
              {withCoords.length - onlineOnMap} off
            </Badge>
            <Badge variant="default" className="bg-primary/10 text-primary">
              <Maximize2 className="mr-1 h-3 w-3" />
              {withCoords.length} vehículos
            </Badge>
          </div>
        </div>
      </CardHeader>
      <CardContent className="p-4">
        <div className="relative h-[min(520px,60vh)] min-h-[400px] overflow-hidden rounded-xl border border-border shadow-inner">
          {withCoords.length > 0 ? (
            <LeafletFleetMap
              vehicles={vehicles}
              selectedDeviceId={selectedDeviceId}
              focusTarget={focusTarget}
              autoFit={autoFit}
            />
          ) : (
            <div className="flex h-full flex-col items-center justify-center gap-3 bg-gradient-to-br from-slate-50 to-sky-50">
              <MapPin className="h-10 w-10 text-slate-300" />
              <p className="text-sm font-medium text-slate-500">Sin coordenadas disponibles</p>
              <p className="text-xs text-slate-400">Selecciona modo demo o conecta el backend</p>
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
