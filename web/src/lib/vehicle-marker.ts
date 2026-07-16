/** Iconos SVG de marcadores de mapa por tipo de vehículo. */
import L from "leaflet";
import type { VehicleStatus } from "@/types/fleet";
import { esVehiculoEnLinea } from "@/lib/labels";
import { normalizeHeading } from "@/lib/geo-bearing";

const ICON_SIZE: [number, number] = [48, 62];
const ICON_ANCHOR: [number, number] = [24, 30];

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
    ? { body: "#22c55e", border: "#15803d", accent: "#bbf7d0" }
    : { body: "#9ca3af", border: "#6b7280", accent: "#e5e7eb" };
}

function vehicleLabel(vehicle: VehicleStatus): string {
  const name = vehicle.vehicleName?.trim();
  return escapeHtml(name && name.length > 0 ? name : "Vehículo");
}

/**
 * Siluetas top-down deliberadamente distintas (viewBox 40×48).
 * data-vehicle-type permite aserciones y depuración visual.
 */
function vehicleSvg(
  type: VehicleStatus["vehicleType"],
  body: string,
  border: string,
  accent: string,
): string {
  switch (type) {
    case "motorcycle":
      // Dos ruedas + chasis estrecho + manillar: no se confunde con un auto.
      return `
        <g data-vehicle-type="motorcycle">
          <ellipse cx="20" cy="42" rx="6" ry="4.5" fill="${border}" stroke="white" stroke-width="1.4"/>
          <ellipse cx="20" cy="8" rx="5" ry="3.8" fill="${border}" stroke="white" stroke-width="1.4"/>
          <rect x="17" y="11" width="6" height="28" rx="3" fill="${body}" stroke="white" stroke-width="1.8"/>
          <rect x="14" y="20" width="12" height="5" rx="2" fill="${accent}" stroke="white" stroke-width="1"/>
          <rect x="12" y="9" width="16" height="3" rx="1.5" fill="${border}" stroke="white" stroke-width="1"/>
        </g>
      `;
    case "van":
      // Caja alta y corta, parabrisas grande.
      return `
        <g data-vehicle-type="van">
          <path d="M20 2 L28 10 H12 Z" fill="${body}" stroke="white" stroke-width="1.4"/>
          <rect x="7" y="10" width="26" height="28" rx="5" fill="${body}" stroke="white" stroke-width="2.2"/>
          <rect x="10" y="13" width="20" height="10" rx="2" fill="${accent}" opacity="0.95"/>
          <rect x="10" y="26" width="20" height="8" rx="1.5" fill="white" opacity="0.35"/>
          <circle cx="12" cy="40" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
          <circle cx="28" cy="40" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
        </g>
      `;
    case "truck":
      // Cabina corta + remolque largo separado.
      return `
        <g data-vehicle-type="truck">
          <path d="M20 2 L27 9 H13 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
          <rect x="10" y="9" width="20" height="12" rx="3" fill="${body}" stroke="white" stroke-width="2"/>
          <rect x="12" y="11" width="16" height="6" rx="1.5" fill="${accent}"/>
          <rect x="6" y="22" width="28" height="18" rx="2" fill="${body}" stroke="white" stroke-width="2.2"/>
          <line x1="20" y1="22" x2="20" y2="40" stroke="white" stroke-width="1.5" opacity="0.5"/>
          <circle cx="12" cy="42" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
          <circle cx="28" cy="42" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
        </g>
      `;
    case "bus":
      // Carrocería alargada con tres ventanas.
      return `
        <g data-vehicle-type="bus">
          <rect x="8" y="3" width="24" height="38" rx="6" fill="${body}" stroke="white" stroke-width="2.2"/>
          <rect x="11" y="7" width="18" height="6" rx="1.5" fill="${accent}"/>
          <rect x="11" y="16" width="18" height="5" rx="1" fill="white" opacity="0.4"/>
          <rect x="11" y="24" width="18" height="5" rx="1" fill="white" opacity="0.4"/>
          <rect x="11" y="32" width="18" height="4" rx="1" fill="white" opacity="0.3"/>
          <circle cx="13" cy="43" r="3.2" fill="${border}" stroke="white" stroke-width="1.2"/>
          <circle cx="27" cy="43" r="3.2" fill="${border}" stroke="white" stroke-width="1.2"/>
        </g>
      `;
    case "pickup":
      // Cabina + caja abierta trasera (hueco).
      return `
        <g data-vehicle-type="pickup">
          <path d="M20 2 L27 9 H13 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
          <rect x="9" y="9" width="22" height="14" rx="4" fill="${body}" stroke="white" stroke-width="2"/>
          <rect x="12" y="12" width="16" height="7" rx="2" fill="${accent}"/>
          <rect x="9" y="24" width="22" height="14" rx="2" fill="none" stroke="${body}" stroke-width="3"/>
          <rect x="12" y="27" width="16" height="8" rx="1" fill="${accent}" opacity="0.35"/>
          <circle cx="13" cy="42" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
          <circle cx="27" cy="42" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
        </g>
      `;
    case "car":
    default:
      // Sedán compacto con parabrisas único.
      return `
        <g data-vehicle-type="car">
          <path d="M20 3 L26 10 H14 Z" fill="${body}" stroke="white" stroke-width="1.2"/>
          <rect x="10" y="10" width="20" height="26" rx="7" fill="${body}" stroke="white" stroke-width="2.2"/>
          <rect x="13" y="13" width="14" height="8" rx="2.5" fill="${accent}"/>
          <rect x="14" y="24" width="12" height="5" rx="1.5" fill="white" opacity="0.35"/>
          <circle cx="14" cy="40" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
          <circle cx="26" cy="40" r="3.5" fill="${border}" stroke="white" stroke-width="1.2"/>
        </g>
      `;
  }
}

/** Icono de vehículo visto desde arriba; la punta apunta al rumbo (0° = norte). */
export function createVehicleMarkerIcon(vehicle: VehicleStatus, selected: boolean): L.DivIcon {
  const online = esVehiculoEnLinea(vehicle.status);
  const { body, border, accent } = markerColors(online);
  const heading = normalizeHeading(vehicle.headingDegrees);
  const scale = selected ? 1.12 : 1;
  const ring = selected
    ? "filter:drop-shadow(0 0 6px #38bdf8);"
    : "filter:drop-shadow(0 2px 4px rgba(15,23,42,0.25));";

  const html = `
    <div style="display:flex;flex-direction:column;align-items:center;background:transparent;border:none;">
      <div style="${ring} transform:rotate(${heading}deg) scale(${scale}); transform-origin:center center;">
        <svg xmlns="http://www.w3.org/2000/svg" width="40" height="48" viewBox="0 0 40 48" aria-hidden="true">
          ${vehicleSvg(vehicle.vehicleType, body, border, accent)}
        </svg>
      </div>
      <span style="
        margin-top:2px;padding:1px 7px;border-radius:9999px;
        background:rgba(255,255,255,0.95);font-size:10px;font-weight:700;
        color:#334155;border:1px solid #e2e8f0;white-space:nowrap;
        box-shadow:0 1px 2px rgba(15,23,42,0.08);
      ">${vehicleLabel(vehicle)}</span>
    </div>
  `;

  return L.divIcon({
    className: "fleet-vehicle-marker",
    html,
    iconSize: ICON_SIZE,
    iconAnchor: ICON_ANCHOR,
  });
}

export { ICON_SIZE, ICON_ANCHOR };
