"use client";

import { useEffect, useMemo, useState } from "react";
import type { VehicleStatus } from "@/types/fleet";
import { snapVehiclesToRoads } from "@/lib/snap-to-road";
import { spreadDistinctVehiclePositions } from "@/lib/spread-vehicle-positions";

export function useSnappedVehicles(vehicles: VehicleStatus[]) {
  const [snapped, setSnapped] = useState<VehicleStatus[]>(vehicles);
  const [snapping, setSnapping] = useState(false);

  const vehiclesKey = useMemo(
    () =>
      vehicles
        .map((v) => `${v.vehicleId}|${v.lastLatitude}|${v.lastLongitude}|${v.status}`)
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
