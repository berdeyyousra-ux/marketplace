import { cn } from "@/lib/cn";

// A dashboard figure: muted label, large tabular number, optional change.
export function StatTile({ label, value, change }: { label: string; value: React.ReactNode; change?: { text: string; direction: "up" | "down" | "flat" } }) {
  return (
    <div className="flex flex-col gap-2 rounded-card border border-border bg-surface p-4">
      <span className="type-label text-muted">{label}</span>
      <span className="text-[28px] font-semibold leading-tight tracking-tight text-text tabular-nums">{value}</span>
      {change && (
        <span className={cn("type-label", change.direction === "up" ? "text-success" : change.direction === "down" ? "text-danger" : "text-muted")}>
          {change.text}
        </span>
      )}
    </div>
  );
}
