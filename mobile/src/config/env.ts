export function getApiBaseUrl(): string {
  return process.env.EXPO_PUBLIC_API_URL ?? "http://localhost:5000";
}

export function getDefaultVehicleId(): string {
  return process.env.EXPO_PUBLIC_VEHICLE_ID ?? "VH-001";
}

export function getDefaultDriverId(): string {
  return process.env.EXPO_PUBLIC_DRIVER_ID ?? "DRV-001";
}
