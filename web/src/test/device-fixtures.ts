/** UUIDs determinísticos para fixtures de tests. */
export const TEST_DEVICE_1 = "00000000-0000-4000-8000-000000000001";
export const TEST_DEVICE_2 = "00000000-0000-4000-8000-000000000002";
export const TEST_DEVICE_3 = "00000000-0000-4000-8000-000000000003";

export function testDeviceId(index: number): string {
  return `00000000-0000-4000-8000-${String(index).padStart(12, "0")}`;
}

export function testVehicleName(index: number): string {
  return `VH-${String(index).padStart(3, "0")}`;
}
