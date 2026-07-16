/** UUIDs determinísticos para fixtures de tests. */
import type { VehicleStatus } from "@/types/fleet";

export const TEST_DEVICE_1 = "00000000-0000-4000-8000-000000000001";
export const TEST_DEVICE_2 = "00000000-0000-4000-8000-000000000002";
export const TEST_DEVICE_3 = "00000000-0000-4000-8000-000000000003";

export function testDeviceId(index: number): string {
  return `00000000-0000-4000-8000-${String(index).padStart(12, "0")}`;
}

export function testVehicleName(index: number): string {
  return `VH-${String(index).padStart(3, "0")}`;
}

/** Vehículo mínimo válido para fixtures de prueba. */
export function testVehicle(
  deviceId: string,
  overrides: Partial<VehicleStatus> = {},
): VehicleStatus {
  const index = Number.parseInt(deviceId.slice(-3), 10) || 1;
  return {
    deviceId,
    vehicleName: overrides.vehicleName ?? testVehicleName(index),
    vehicleType: overrides.vehicleType ?? "car",
    status: "online",
    lastSeenAt: "2026-07-10T10:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
    ...overrides,
  };
}
