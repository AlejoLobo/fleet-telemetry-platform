// Configuración de entorno con valores por defecto

// URL base de la API de telemetría
export function getApiBaseUrl(): string {
  return process.env.EXPO_PUBLIC_API_URL ?? "http://localhost:5000";
}

// ID de vehículo por defecto
export function getDefaultVehicleId(): string {
  return process.env.EXPO_PUBLIC_VEHICLE_ID ?? "VH-001";
}

// ID de conductor por defecto
export function getDefaultDriverId(): string {
  return process.env.EXPO_PUBLIC_DRIVER_ID ?? "DRV-001";
}
