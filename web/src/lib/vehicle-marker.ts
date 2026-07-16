/** Iconos SVG de marcadores de mapa por tipo de vehículo. */
import L from "leaflet";
import type { VehicleStatus } from "@/types/fleet";
import { esVehiculoEnLinea } from "@/lib/labels";
import { normalizeHeading } from "@/lib/geo-bearing";

const ICON_SIZE: [number, number] = [44, 56];
const ICON_ANCHOR: [number, number] = [22, 28];

/** Escapa texto para insertarlo en HTML de marcadores Leaflet. */
export function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function markerColors(online: boolean) {
  return online
    ? { body: "#22c55e", border: "#15803d" }
    : { body: "#9ca3af", border: "#6b7280" };
}

function vehicleLabel(vehicle: VehicleStatus): string {
  const name = vehicle.vehicleName?.trim();
  return escapeHtml(name && name.length > 0 ? name : "Vehículo");
}

/** SVG visto desde arriba por tipo de vehículo (34×42 viewBox). */
function vehicleSvg(type: VehicleStatus["vehicleType"], body: string, border: string): string {
  switch (type) {
    case "motorcycle":
      return `
        <ellipse cx="17" cy="34" rx="4.5" ry="3.2" fill="${border}" stroke="white" stroke-width="1"/>
        <ellipse cx="17" cy="10" rx="3.2" ry="2.4" fill="${border}" stroke="white" stroke-width="1"/>
        <rect x="14.5" y="12" width="5" height="20" rx="2.5" fill="${body}" stroke="white" stroke-width="1.6"/>
        <rect x="13" y="18" width="8" height="4" rx="1.5" fill="white" opacity="0.55"/>
      `;
    case "van":
      return `
        <path d="M17 1 L22 8 H12 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
        <rect x="6" y="8" width="22" height="24" rx="4" fill="${body}" stroke="white" stroke-width="2"/>
        <rect x="8" y="11" width="18" height="8" rx="2" fill="white" opacity="0.55"/>
        <rect x="8" y="24" width="18" height="6" rx="1" fill="white" opacity="0.35"/>
        <circle cx="10" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
        <circle cx="24" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
      `;
    case "truck":
      return `
        <path d="M17 1 L22 8 H12 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
        <rect x="4" y="8" width="26" height="24" rx="3" fill="${body}" stroke="white" stroke-width="2"/>
        <rect x="6" y="11" width="10" height="10" rx="2" fill="white" opacity="0.55"/>
        <rect x="18" y="11" width="10" height="18" rx="1.5" fill="white" opacity="0.3"/>
        <circle cx="9" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
        <circle cx="25" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
      `;
    case "bus":
      return `
        <rect x="5" y="4" width="24" height="30" rx="5" fill="${body}" stroke="white" stroke-width="2"/>
        <rect x="8" y="8" width="18" height="7" rx="2" fill="white" opacity="0.55"/>
        <rect x="8" y="18" width="18" height="4" rx="1" fill="white" opacity="0.35"/>
        <rect x="8" y="24" width="18" height="4" rx="1" fill="white" opacity="0.35"/>
        <circle cx="10" cy="36" r="3" fill="${border}" stroke="white" stroke-width="1"/>
        <circle cx="24" cy="36" r="3" fill="${border}" stroke="white" stroke-width="1"/>
      `;
    case "pickup":
      return `
        <path d="M17 1 L22 8 H12 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
        <rect x="7" y="8" width="20" height="14" rx="4" fill="${body}" stroke="white" stroke-width="2"/>
        <rect x="9" y="11" width="16" height="6" rx="2" fill="white" opacity="0.55"/>
        <rect x="7" y="22" width="20" height="10" rx="2" fill="${body}" stroke="white" stroke-width="1.5"/>
        <circle cx="11" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
        <circle cx="23" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
      `;
    case "car":
    default:
      return `
        <path d="M17 1 L22 8 H12 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
        <rect x="8" y="8" width="18" height="24" rx="5" fill="${body}" stroke="white" stroke-width="2"/>
        <rect x="10" y="11" width="14" height="7" rx="2" fill="white" opacity="0.55"/>
        <circle cx="11" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
        <circle cx="23" cy="34" r="3" fill="${border}" stroke="white" stroke-width="1"/>
      `;
  }
}

/** Icono de vehículo visto desde arriba; la punta apunta al rumbo (0° = norte). */
export function createVehicleMarkerIcon(vehicle: VehicleStatus, selected: boolean): L.DivIcon {
  const online = esVehiculoEnLinea(vehicle.status);
  const { body, border } = markerColors(online);
  const heading = normalizeHeading(vehicle.headingDegrees);
  const scale = selected ? 1.12 : 1;
  const ring = selected
    ? "filter:drop-shadow(0 0 6px #38bdf8);"
    : "filter:drop-shadow(0 2px 4px rgba(15,23,42,0.2));";

  const html = `
    <div style="display:flex;flex-direction:column;align-items:center;">
      <div style="${ring} transform:rotate(${heading}deg) scale(${scale}); transform-origin:center center;">
        <svg xmlns="http://www.w3.org/2000/svg" width="34" height="42" viewBox="0 0 34 42" aria-hidden="true">
          ${vehicleSvg(vehicle.vehicleType, body, border)}
        </svg>
      </div>
      <span style="
        margin-top:2px;padding:1px 7px;border-radius:9999px;
        background:rgba(255,255,255,0.95);font-size:10px;font-weight:700;
        color:#334155;border:1px solid #e2e8f0;white-space:nowrap;
      ">${vehicleLabel(vehicle)}</span>
    </div>
  `;

  return L.divIcon({
    className: "",
    html,
    iconSize: ICON_SIZE,
    iconAnchor: ICON_ANCHOR,
  });
}

export { ICON_SIZE, ICON_ANCHOR };
