import type { SseConnectionState } from "@/types/fleet";
import { Badge } from "@/components/ui/badge";

const labels: Record<SseConnectionState, string> = {
  connected: "SSE conectado",
  reconnecting: "SSE reconectando",
  disconnected: "SSE desconectado",
};

const variants: Record<SseConnectionState, "success" | "warning" | "outline"> = {
  connected: "success",
  reconnecting: "warning",
  disconnected: "outline",
};

export function ConnectionStatus({ state, usingMock }: { state: SseConnectionState; usingMock: boolean }) {
  return (
    <div className="flex items-center gap-2">
      <Badge variant={variants[state]}>{labels[state]}</Badge>
      {usingMock && <Badge variant="outline">Modo mock</Badge>}
    </div>
  );
}
