/** Componentes UI reutilizables: insignia de estado. */
import * as React from "react";
import { cn } from "@/lib/utils";

/** Insignia con colores según severidad o estado. */
export function Badge({
  className,
  variant = "default",
  ...props
}: React.HTMLAttributes<HTMLSpanElement> & { variant?: "default" | "success" | "warning" | "critical" | "outline" }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium",
        variant === "default" && "bg-primary/10 text-primary",
        variant === "success" && "bg-emerald-500/15 text-emerald-700",
        variant === "warning" && "bg-amber-500/15 text-amber-700",
        variant === "critical" && "bg-red-500/15 text-red-700",
        variant === "outline" && "border border-border text-muted-foreground bg-white",
        className,
      )}
      {...props}
    />
  );
}
