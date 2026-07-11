jest.mock("expo-location", () => ({
  Accuracy: { Balanced: 3 },
  requestForegroundPermissionsAsync: jest.fn(),
  getCurrentPositionAsync: jest.fn(),
}));

const mockIsSimulatedAllowed = jest.fn(() => false);
jest.mock("@/config/env", () => ({
  isSimulatedLocationAllowed: () => mockIsSimulatedAllowed(),
}));

import * as Location from "expo-location";
import { getCurrentReading, LocationCaptureError } from "@/services/location-provider";

describe("location-provider", () => {
  beforeEach(() => {
    mockIsSimulatedAllowed.mockReturnValue(false);
    jest.clearAllMocks();
  });

  it("rechaza GPS denegado sin simulación habilitada", async () => {
    (Location.requestForegroundPermissionsAsync as jest.Mock).mockResolvedValue({ status: "denied" });

    await expect(getCurrentReading()).rejects.toBeInstanceOf(LocationCaptureError);
    await expect(getCurrentReading()).rejects.toThrow("Permiso de ubicación denegado");
  });

  it("marca lectura simulada solo con flag explícito", async () => {
    mockIsSimulatedAllowed.mockReturnValue(true);
    (Location.requestForegroundPermissionsAsync as jest.Mock).mockResolvedValue({ status: "denied" });

    const reading = await getCurrentReading();
    expect(reading.source).toBe("simulated");
  });

  it("propaga lectura GPS real", async () => {
    (Location.requestForegroundPermissionsAsync as jest.Mock).mockResolvedValue({ status: "granted" });
    (Location.getCurrentPositionAsync as jest.Mock).mockResolvedValue({
      coords: { latitude: 4.65, longitude: -74.08, speed: 10 },
    });

    const reading = await getCurrentReading();
    expect(reading.source).toBe("gps");
    expect(reading.latitude).toBe(4.65);
  });
});
