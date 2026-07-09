"use client";

import { MapContainer, TileLayer, Marker, Popup, useMap } from "react-leaflet";
import L from "leaflet";
import { useEffect } from "react";
import type { VehicleStatus } from "@/types/fleet";
import { OSM_TILE_ATTRIBUTION, OSM_TILE_URL, getMapBounds } from "@/lib/map-config";
import { createCarMarkerIcon } from "@/lib/vehicle-car-marker";
import { useSnappedVehicles } from "@/hooks/use-snapped-vehicles";
import { etiquetaEstadoVehiculo } from "@/lib/labels";
import "leaflet/dist/leaflet.css";

type LeafletFleetMapProps = {
  vehicles: VehicleStatus[];
  selectedVehicleId?: string;
};

function FitBounds({ vehicles }: { vehicles: VehicleStatus[] }) {
  const map = useMap();

  useEffect(() => {
    const coords = vehicles
      .filter((v) => v.lastLatitude != null && v.lastLongitude != null)
      .map((v) => [v.lastLatitude!, v.lastLongitude!] as [number, number]);

    if (coords.length === 0) return;

    if (coords.length === 1) {
      map.setView(coords[0], 15);
      return;
    }

    map.fitBounds(L.latLngBounds(coords), { padding: [48, 48], maxZoom: 15 });
  }, [map, vehicles]);

  return null;
}

export function LeafletFleetMap({ vehicles, selectedVehicleId }: LeafletFleetMapProps) {
  const { vehicles: snappedVehicles, snapping } = useSnappedVehicles(vehicles);
  const positioned = snappedVehicles.filter(
    (v) => v.lastLatitude != null && v.lastLongitude != null,
  );
  const { center, zoom } = getMapBounds(snappedVehicles);

  return (
    <div className="relative h-full w-full">
      {snapping && (
        <div className="absolute left-3 top-3 z-[1000] rounded-full bg-white/95 px-3 py-1 text-xs text-slate-600 shadow-sm">
          Ajustando a calles…
        </div>
      )}
      <MapContainer
        center={[center.lat, center.lng]}
        zoom={zoom}
        className="h-full w-full rounded-xl z-0"
        scrollWheelZoom
      >
        <TileLayer url={OSM_TILE_URL} attribution={OSM_TILE_ATTRIBUTION} />
        <FitBounds vehicles={snappedVehicles} />
        {positioned.map((vehicle) => (
          <Marker
            key={vehicle.vehicleId}
            position={[vehicle.lastLatitude!, vehicle.lastLongitude!]}
            icon={createCarMarkerIcon(vehicle, vehicle.vehicleId === selectedVehicleId)}
          >
            <Popup>
              <strong>{vehicle.vehicleId}</strong>
              {vehicle.name && (
                <>
                  <br />
                  <span className="text-slate-600">{vehicle.name}</span>
                </>
              )}
              <br />
              <span className="text-slate-500">{etiquetaEstadoVehiculo(vehicle.status)}</span>
              <br />
              <span className="text-xs text-slate-400">
                {vehicle.lastLatitude?.toFixed(5)}, {vehicle.lastLongitude?.toFixed(5)}
              </span>
            </Popup>
          </Marker>
        ))}
      </MapContainer>
    </div>
  );
}
