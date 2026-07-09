import type { TelemetryEvent } from "@/types/fleet";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function TelemetryTable({ events, vehicleId }: { events: TelemetryEvent[]; vehicleId: string }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Telemetría — {vehicleId}</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border text-left text-muted-foreground">
                <th className="pb-2 pr-4">Timestamp</th>
                <th className="pb-2 pr-4">Speed</th>
                <th className="pb-2 pr-4">Fuel</th>
                <th className="pb-2 pr-4">Battery</th>
                <th className="pb-2">Coords</th>
              </tr>
            </thead>
            <tbody>
              {events.length === 0 && (
                <tr>
                  <td colSpan={5} className="py-4 text-muted-foreground">
                    Sin eventos en las últimas 24 h.
                  </td>
                </tr>
              )}
              {events.map((event) => (
                <tr key={event.eventId} className="border-b border-border/50">
                  <td className="py-2 pr-4">{new Date(event.timestamp).toLocaleString()}</td>
                  <td className="py-2 pr-4">{event.speedKmh.toFixed(1)} km/h</td>
                  <td className="py-2 pr-4">{event.fuelLevelPercent?.toFixed(1) ?? "—"}%</td>
                  <td className="py-2 pr-4">{event.batteryPercent?.toFixed(1) ?? "—"}%</td>
                  <td className="py-2">{event.latitude.toFixed(4)}, {event.longitude.toFixed(4)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}
