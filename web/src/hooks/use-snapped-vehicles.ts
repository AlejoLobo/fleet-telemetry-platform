/** Hook para ajustar posiciones de vehículos a calles (OSRM). */
"use client";

import { useEffect, useMemo, useState } from "react";
import type { VehicleStatus } from "@/types/fleet";
import { snapVehiclesToRoads } from "@/lib/snap-to-road";
import { spreadDistinctVehiclePositions } from "@/lib/spread-vehicle-positions";

/** Ajusta vehículos a vías y los separa si coinciden. */
export function useSnappedVehicles(vehicles: VehicleStatus[]) {
  const [snapped, setSnapped] = useState<VehicleStatus[]>(vehicles);
  const [snapping, setSnapping] = useState(false);

  const vehiclesKey = useMemo(
    () =>
      vehicles
        .map(
          (v) =>
            `${v.deviceId}|${v.vehicleType}|${v.vehicleName}|${v.lastLatitude}|${v.lastLongitude}|${v.status}|${v.headingDegrees ?? ""}`,
        )
        .join(";"),
    [vehicles],
  );

  useEffect(() => {
    let cancelled = false;

    const run = async () => {
      setSnapping(true);
      const adjusted = spreadDistinctVehiclePositions(await snapVehiclesToRoads(vehicles));
      if (!cancelled) {
        setSnapped(adjusted);
        setSnapping(false);
      }
    };

    run();
    return () => {
      cancelled = true;
    };
  }, [vehiclesKey, vehicles]);

  return { vehicles: snapped, snapping };
}
