import { cn } from "@/lib/cn";

// 1px border, 10px radius, no shadow. Interactive cards change only their border colour on hover.
export function Card({ interactive = false, className, ...rest }: React.HTMLAttributes<HTMLDivElement> & { interactive?: boolean }) {
  return (
    <div
      {...rest}
      className={cn("rounded-card border border-border bg-surface", interactive && "hover:border-accent focus-within:border-accent", className)}
    />
  );
}
