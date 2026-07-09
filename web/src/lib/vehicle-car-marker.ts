import L from "leaflet";
import type { VehicleStatus } from "@/types/fleet";
import { esVehiculoEnLinea } from "@/lib/labels";
import { normalizeHeading } from "@/lib/geo-bearing";

/** Icono de auto visto desde arriba; la punta del vehículo apunta al rumbo (0° = norte) */
export function createCarMarkerIcon(vehicle: VehicleStatus, selected: boolean): L.DivIcon {
  const online = esVehiculoEnLinea(vehicle.status);
  const body = online ? "#22c55e" : "#9ca3af";
  const border = online ? "#15803d" : "#6b7280";
  const heading = normalizeHeading(vehicle.headingDegrees);
  const scale = selected ? 1.12 : 1;
  const ring = selected ? "filter:drop-shadow(0 0 4px #38bdf8);" : "filter:drop-shadow(0 2px 4px rgba(15,23,42,0.2));";

  const html = `
    <div style="display:flex;flex-direction:column;align-items:center;">
      <div style="${ring} transform:rotate(${heading}deg) scale(${scale}); transform-origin:center center;">
        <svg xmlns="http://www.w3.org/2000/svg" width="34" height="42" viewBox="0 0 34 42" aria-hidden="true">
          <path d="M17 1 L22 8 H12 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
          <rect x="8" y="8" width="18" height="24" rx="5" fill="${body}" stroke="white" stroke-width="2"/>
          <rect x="10" y="11" width="14" height="7" rx="2" fill="white" opacity="0.55"/>
          <circle cx="11" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
          <circle cx="23" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
        </svg>
      </div>
      <span style="
        margin-top:2px;padding:1px 7px;border-radius:9999px;
        background:rgba(255,255,255,0.95);font-size:10px;font-weight:700;
        color:#334155;border:1px solid #e2e8f0;white-space:nowrap;
      ">${vehicle.vehicleId}</span>
    </div>
  `;

  return L.divIcon({
    className: "",
    html,
    iconSize: [44, 56],
    iconAnchor: [22, 28],
  });
}
