export function getApiBaseUrl(): string { return process.env.EXPO_PUBLIC_API_URL ?? "http://localhost:5000"; }
export function getDefaultDriverId(): string { return process.env.EXPO_PUBLIC_DRIVER_ID ?? "DRV-001"; }
export function isSimulatedLocationAllowed(): boolean { return process.env.EXPO_PUBLIC_ALLOW_SIMULATED_LOCATION === "true"; }
