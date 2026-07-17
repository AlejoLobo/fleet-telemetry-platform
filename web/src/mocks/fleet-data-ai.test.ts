import { describe, expect, it, beforeEach } from "vitest";
import {
  generateMockAiResponse,
  refreshMockDataset,
  resetMockDatasetForTests,
} from "@/mocks/fleet-data";

describe("generateMockAiResponse modo demo por intención", () => {
  beforeEach(() => {
    resetMockDatasetForTests();
    refreshMockDataset(8);
  });

  it("responde distinto para resumen, en línea, alertas, detenidos y velocidad", () => {
    const overview = generateMockAiResponse("Resumen de la flota");
    const online = generateMockAiResponse("¿Cuántos vehículos hay en línea?");
    const critical = generateMockAiResponse("¿Qué vehículos tienen alertas críticas?");
    const stopped = generateMockAiResponse("¿Cuáles están detenidos?");
    const speed = generateMockAiResponse("Vehículos por encima de 80 km/h");

    expect(overview.sources).toContain("GetFleetOverview");
    expect(online.sources).toEqual(["GetFleetOverview"]);
    expect(critical.sources).toEqual(["GetVehiclesWithCriticalAlerts"]);
    expect(stopped.sources).toEqual(["GetStoppedVehicles"]);
    expect(speed.sources).toEqual(["GetVehiclesAboveSpeed"]);

    expect(overview.answer).toContain("Resumen operativo");
    expect(online.answer.toLowerCase()).toContain("en línea");
    expect(online.answer).not.toContain("Resumen operativo");

    const answers = new Set([
      overview.answer,
      online.answer,
      critical.answer,
      stopped.answer,
      speed.answer,
    ]);
    expect(answers.size).toBe(5);
  });

  it("rechaza preguntas no operativas", () => {
    const response = generateMockAiResponse("cuéntame un chiste");
    expect(response.sources).toEqual([]);
    expect(response.answer.toLowerCase()).toContain("operativas");
  });
});
