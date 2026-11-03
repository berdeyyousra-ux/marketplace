import { cn } from "@/lib/cn";

// Placeholders with the real content's dimensions, so nothing shifts when it arrives.
function Block({ className }: { className?: string }) {
  return <div className={cn("animate-pulse rounded-control bg-border", className)} />;
}

export function SkeletonCard() {
  return (
    <div className="overflow-hidden rounded-card border border-border bg-surface" aria-hidden="true">
      <Block className="aspect-[4/3] w-full rounded-none" />
      <div className="flex flex-col gap-2 p-4">
        <Block className="h-4 w-4/5" />
        <Block className="h-3 w-1/2" />
        <Block className="mt-2 h-5 w-1/4" />
      </div>
    </div>
  );
}

export function SkeletonRow() {
  return (
    <div className="flex items-center gap-4 border-b border-border py-3" aria-hidden="true">
      <Block className="size-12 shrink-0" />
      <Block className="h-4 flex-1" />
      <Block className="h-8 w-24" />
    </div>
  );
}

export function SkeletonText({ lines = 3 }: { lines?: number }) {
  return (
    <div className="flex flex-col gap-2" aria-hidden="true">
      {Array.from({ length: lines }, (_, index) => <Block key={index} className={cn("h-4", index === lines - 1 ? "w-3/5" : "w-full")} />)}
    </div>
  );
}
