import type { FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";

export const mockVehicles: VehicleStatus[] = [
  {
    vehicleId: "VH-001",
    name: "Delivery Truck 01",
    status: "online",
    lastSeenAt: new Date().toISOString(),
    lastSpeedKmh: 42.5,
    lastLatitude: 4.6533,
    lastLongitude: -74.0836,
  },
  {
    vehicleId: "VH-002",
    name: "Delivery Truck 02",
    status: "online",
    lastSeenAt: new Date(Date.now() - 120_000).toISOString(),
    lastSpeedKmh: 0,
    lastLatitude: 4.6612,
    lastLongitude: -74.0951,
  },
  {
    vehicleId: "VH-003",
    name: "Van 03",
    status: "offline",
    lastSeenAt: new Date(Date.now() - 600_000).toISOString(),
    lastSpeedKmh: 18.0,
    lastLatitude: 4.7102,
    lastLongitude: -74.0722,
  },
];

export const mockAlerts: FleetAlert[] = [
  {
    alertId: "a1111111-1111-1111-1111-111111111111",
    vehicleId: "VH-001",
    alertType: "overspeed",
    severity: "critical",
    message: "Vehicle VH-001 exceeded speed limit: 130.0 km/h",
    createdAt: new Date().toISOString(),
    isAcknowledged: false,
  },
  {
    alertId: "a2222222-2222-2222-2222-222222222222",
    vehicleId: "VH-001",
    alertType: "low_fuel",
    severity: "warning",
    message: "Vehicle VH-001 has low fuel: 10.0%",
    createdAt: new Date(Date.now() - 300_000).toISOString(),
    isAcknowledged: false,
  },
];

export const mockTelemetry: TelemetryEvent[] = [
  {
    eventId: "11111111-1111-1111-1111-111111111111",
    vehicleId: "VH-001",
    driverId: "DRV-001",
    timestamp: new Date().toISOString(),
    latitude: 4.6533,
    longitude: -74.0836,
    speedKmh: 130,
    fuelLevelPercent: 10,
    batteryPercent: 95,
  },
  {
    eventId: "22222222-2222-2222-2222-222222222222",
    vehicleId: "VH-002",
    driverId: "DRV-002",
    timestamp: new Date(Date.now() - 60_000).toISOString(),
    latitude: 4.6612,
    longitude: -74.0951,
    speedKmh: 0,
    fuelLevelPercent: 55,
    batteryPercent: 88,
  },
];

export const mockAiResponse = {
  answer:
    "Hay 1 alerta crítica abierta para VH-001 (overspeed). 2 vehículos online de 3 en flota.",
  sources: ["GetFleetOverview", "GetVehiclesWithCriticalAlerts"],
};
