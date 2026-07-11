"use client";

import { useMemo } from "react";
import type { FleetDataSource } from "@/lib/analytics";
import type { VehicleStatus } from "@/types/fleet";

export function useDisplayFleet(
  dataSource: FleetDataSource,
  apiVehicles: VehicleStatus[],
  liveVehicles: VehicleStatus[] | null,
) {
  return useMemo(() => {
    if (dataSource === "demo") return apiVehicles;
    if (liveVehicles !== null) return liveVehicles;
    return apiVehicles;
  }, [apiVehicles, dataSource, liveVehicles]);
}
