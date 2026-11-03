import { cn } from "@/lib/cn";

// A dashboard figure: muted label, large number, optional change. Proportional figures: tabular ones look loose at
// this size and only help when numbers line up in a column (dataviz rules).
export function StatTile({ label, value, change, detail }: {
  label: string; value: React.ReactNode; change?: { text: string; direction: "up" | "down" | "flat" }; detail?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-2 rounded-card border border-border bg-surface p-4">
      <span className="type-label text-muted">{label}</span>
      <span className="text-[28px] font-semibold leading-tight tracking-tight text-text">{value}</span>
      {detail && <span className="type-label text-muted">{detail}</span>}
      {change && (
        <span className={cn("type-label", change.direction === "up" ? "text-success" : change.direction === "down" ? "text-danger" : "text-muted")}>
          {change.text}
        </span>
      )}
    </div>
  );
}
