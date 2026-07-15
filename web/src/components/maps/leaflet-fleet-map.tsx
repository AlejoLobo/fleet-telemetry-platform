/** Mapa Leaflet con marcadores de vehículos y ajuste a calles. */
"use client";

import { MapContainer, TileLayer, Marker, Popup, useMap } from "react-leaflet";
import L from "leaflet";
import { useEffect, useRef } from "react";
import type { VehicleStatus } from "@/types/fleet";
import { OSM_TILE_ATTRIBUTION, OSM_TILE_URL, getMapBounds } from "@/lib/map-config";
import { createCarMarkerIcon } from "@/lib/vehicle-car-marker";
import { useSnappedVehicles } from "@/hooks/use-snapped-vehicles";
import { etiquetaEstadoVehiculo, formatVehicleDisplayName } from "@/lib/labels";
import "leaflet/dist/leaflet.css";

export type MapFocusTarget = {
  deviceId: string;
  tick: number;
};

type LeafletFleetMapProps = {
  vehicles: VehicleStatus[];
  selectedDeviceId?: string | null;
  focusTarget?: MapFocusTarget | null;
  autoFit?: boolean;
};

/** Ajusta el zoom para mostrar todos los vehículos. */
function FitBounds({
  vehicles,
  enabled,
}: {
  vehicles: VehicleStatus[];
  enabled: boolean;
}) {
  const map = useMap();
  const hasFittedRef = useRef(false);

  useEffect(() => {
    if (!enabled) return;

    const coords = vehicles
      .filter((v) => v.lastLatitude != null && v.lastLongitude != null)
      .map((v) => [v.lastLatitude!, v.lastLongitude!] as [number, number]);

    if (coords.length === 0) return;

    if (coords.length === 1) {
      map.setView(coords[0], 15);
      hasFittedRef.current = true;
      return;
    }

    if (!hasFittedRef.current || enabled) {
      map.fitBounds(L.latLngBounds(coords), { padding: [48, 48], maxZoom: 15 });
      hasFittedRef.current = true;
    }
  }, [map, vehicles, enabled]);

  useEffect(() => {
    if (enabled) hasFittedRef.current = false;
  }, [enabled]);

  return null;
}

/** Vuela al vehículo seleccionado en el mapa. */
function FocusVehicle({
  focusTarget,
  vehicles,
}: {
  focusTarget?: MapFocusTarget | null;
  vehicles: VehicleStatus[];
}) {
  const map = useMap();

  useEffect(() => {
    if (!focusTarget) return;

    const vehicle = vehicles.find((v) => v.deviceId === focusTarget.deviceId);
    if (vehicle?.lastLatitude == null || vehicle.lastLongitude == null) return;

    map.flyTo([vehicle.lastLatitude, vehicle.lastLongitude], 17, {
      duration: 0.85,
      easeLinearity: 0.25,
    });
  }, [focusTarget, map, vehicles]);

  return null;
}

/** Mapa interactivo con marcadores de flota. */
export function LeafletFleetMap({
  vehicles,
  selectedDeviceId,
  focusTarget,
  autoFit = true,
}: LeafletFleetMapProps) {
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
        <FitBounds vehicles={snappedVehicles} enabled={autoFit} />
        <FocusVehicle focusTarget={focusTarget} vehicles={snappedVehicles} />
        {positioned.map((vehicle) => {
          const displayName = formatVehicleDisplayName(vehicle);
          return (
            <Marker
              key={vehicle.deviceId}
              position={[vehicle.lastLatitude!, vehicle.lastLongitude!]}
              icon={createCarMarkerIcon(vehicle, vehicle.deviceId === selectedDeviceId)}
            >
              <Popup>
                <strong>{displayName}</strong>
                {vehicle.vehicleName && vehicle.vehicleName !== displayName && (
                  <>
                    <br />
                    <span className="text-slate-600">{vehicle.vehicleName}</span>
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
          );
        })}
      </MapContainer>
    </div>
  );
}
