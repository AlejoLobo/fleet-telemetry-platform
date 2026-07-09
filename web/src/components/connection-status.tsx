import type { SseConnectionState } from "@/types/fleet";
import { cn } from "@/lib/utils";
import { Wifi, WifiOff, Loader2 } from "lucide-react";

const labels: Record<SseConnectionState, string> = {
  connected: "En vivo",
  reconnecting: "Reconectando",
  disconnected: "Sin conexión",
};

type ConnectionStatusProps = {
  state: SseConnectionState;
  dataSource?: "api" | "demo" | null;
};

export function ConnectionStatus({ state, dataSource }: ConnectionStatusProps) {
  const isLive = state === "connected" && dataSource === "api";

  return (
    <div
      className={cn(
        "flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-medium",
        isLive && "border-emerald-200 bg-emerald-50 text-emerald-700",
        state === "reconnecting" && "border-amber-200 bg-amber-50 text-amber-700",
        state === "disconnected" && dataSource === "api" && "border-slate-200 bg-slate-50 text-slate-600",
        dataSource === "demo" && "border-violet-200 bg-violet-50 text-violet-700",
      )}
    >
      {state === "reconnecting" ? (
        <Loader2 className="h-3.5 w-3.5 animate-spin" />
      ) : isLive ? (
        <Wifi className="h-3.5 w-3.5" />
      ) : dataSource === "demo" ? (
        <span className="h-2 w-2 rounded-full bg-violet-500" />
      ) : (
        <WifiOff className="h-3.5 w-3.5" />
      )}
      {dataSource === "demo" ? "Modo demostración" : labels[state]}
    </div>
  );
}
