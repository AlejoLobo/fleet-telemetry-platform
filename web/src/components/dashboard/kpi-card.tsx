import type { ElementType } from "react";
import { cn } from "@/lib/utils";

type KpiCardProps = {
  icon: ElementType;
  label: string;
  value: string;
  sublabel?: string;
  trend?: "up" | "down" | "neutral";
  accent: "sky" | "emerald" | "amber" | "violet";
};

const accentStyles = {
  sky: {
    icon: "bg-sky-100 text-sky-700",
    ring: "ring-sky-200",
    glow: "from-sky-100/80 to-transparent",
  },
  emerald: {
    icon: "bg-emerald-100 text-emerald-700",
    ring: "ring-emerald-200",
    glow: "from-emerald-100/80 to-transparent",
  },
  amber: {
    icon: "bg-amber-100 text-amber-700",
    ring: "ring-amber-200",
    glow: "from-amber-100/80 to-transparent",
  },
  violet: {
    icon: "bg-violet-100 text-violet-700",
    ring: "ring-violet-200",
    glow: "from-violet-100/80 to-transparent",
  },
};

export function KpiCard({ icon: Icon, label, value, sublabel, accent }: KpiCardProps) {
  const styles = accentStyles[accent];

  return (
    <div
      className={cn(
        "group relative overflow-hidden rounded-2xl border border-border bg-card p-5 shadow-soft",
        "ring-1 ring-slate-200/80 transition-all duration-300 hover:-translate-y-0.5 hover:shadow-card",
        styles.ring,
      )}
    >
      <div
        className={cn(
          "pointer-events-none absolute inset-0 bg-gradient-to-br opacity-60",
          styles.glow,
        )}
      />
      <div className="relative flex items-start justify-between gap-3">
        <div className="space-y-2">
          <p className="text-xs font-medium uppercase tracking-wider text-slate-500">{label}</p>
          <p className="text-3xl font-bold tracking-tight text-slate-900">{value}</p>
          {sublabel && <p className="text-xs text-slate-500">{sublabel}</p>}
        </div>
        <div
          className={cn(
            "flex h-11 w-11 shrink-0 items-center justify-center rounded-xl transition-transform group-hover:scale-105",
            styles.icon,
          )}
        >
          <Icon className="h-5 w-5" />
        </div>
      </div>
    </div>
  );
}
