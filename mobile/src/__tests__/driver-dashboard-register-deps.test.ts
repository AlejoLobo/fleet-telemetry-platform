import fs from "fs";
import path from "path";
import { DEFAULT_VEHICLE_TYPE } from "@/types/vehicle";

function extractRegisterEffectDependencySource(source: string): string | null {
  const match = source.match(
    /ensureDeviceRegistered\(\s*[\s\S]*?\);\s*[\s\S]*?\}, \[([^\]]+)\]/,
  );
  return match?.[1]?.replace(/\s+/g, "") ?? null;
}

function registrationShouldIgnoreProfileFields(depsCsv: string): boolean {
  const deps = depsCsv.split(",").map((d) => d.trim()).filter(Boolean);
  const forbidden = ["vehicleType", "vehicleName", "vehicleTypeDraft", "vehicleNameDraft"];
  return (
    deps.includes("deviceId")
    && deps.includes("identityError")
    && deps.includes("canSync")
    && deps.includes("isOnline")
    && forbidden.every((f) => !deps.includes(f))
  );
}

describe("DriverDashboard registro remoto", () => {
  const source = fs.readFileSync(
    path.join(__dirname, "../components/DriverDashboard.tsx"),
    "utf8",
  );

  it("efecto de registro no depende de vehicleType ni drafts de perfil", () => {
    expect(source).toContain("initialVehicleTypeRef");
    const deps = extractRegisterEffectDependencySource(source);
    expect(deps).toBeTruthy();
    expect(registrationShouldIgnoreProfileFields(deps!)).toBe(true);
    expect(deps).not.toContain("vehicleType");
    expect(deps).not.toContain("vehicleNameDraft");
  });

  it("registro inicial usa tipo cacheado vía ref, no el estado mutable", () => {
    expect(source).toContain("initialVehicleTypeRef.current = cachedType");
    expect(source).toContain("ensureDeviceRegistered(\n          deviceId,\n          initialVehicleTypeRef.current");
    expect(initialTypeForRegistration(null)).toBe(DEFAULT_VEHICLE_TYPE);
  });
});

function initialTypeForRegistration(cachedType: string | null | undefined): string {
  return cachedType ?? DEFAULT_VEHICLE_TYPE;
}
