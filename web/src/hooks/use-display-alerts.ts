"use client";

import { useMemo } from "react";
import { dedupeAlerts } from "@/lib/alert-dedup";
import type { FleetDataSource } from "@/lib/analytics";
import type { FleetAlert } from "@/types/fleet";

export function useDisplayAlerts(
  dataSource: FleetDataSource,
  apiAlerts: FleetAlert[],
  liveAlerts: FleetAlert[],
) {
  return useMemo(() => {
    if (dataSource === "demo") return apiAlerts;
    return dedupeAlerts([...liveAlerts, ...apiAlerts]);
  }, [apiAlerts, dataSource, liveAlerts]);
}
