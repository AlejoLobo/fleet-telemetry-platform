import * as React from "react";
import { cn } from "@/lib/utils";

export function Button({
  className,
  variant = "default",
  size = "default",
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "default" | "secondary" | "outline" | "ghost";
  size?: "default" | "sm" | "icon";
}) {
  return (
    <button
      className={cn(
        "inline-flex items-center justify-center gap-2 rounded-xl text-sm font-medium transition-all duration-200",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
        "disabled:pointer-events-none disabled:opacity-50",
        size === "default" && "h-10 px-4 py-2",
        size === "sm" && "h-8 rounded-lg px-3 text-xs",
        size === "icon" && "h-9 w-9 rounded-lg p-0",
        variant === "default" &&
          "bg-primary text-primary-foreground shadow-soft hover:bg-primary/90 hover:shadow-glow active:scale-[0.98]",
        variant === "secondary" &&
          "bg-secondary text-secondary-foreground hover:bg-secondary/80",
        variant === "outline" &&
          "border border-border bg-white hover:border-primary/40 hover:bg-primary/5",
        variant === "ghost" && "hover:bg-slate-100 text-slate-600",
        className,
      )}
      {...props}
    />
  );
}
