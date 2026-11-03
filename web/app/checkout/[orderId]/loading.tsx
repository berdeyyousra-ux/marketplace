import { SkeletonRow } from "@/components/ui/Skeleton";

export default function Loading() {
  return (
    <div className="mx-auto flex w-full max-w-[480px] flex-col gap-6 px-6 py-8" aria-busy="true" aria-label="Loading the order">
      <div className="flex flex-col gap-2">
        <div className="h-10 w-40 animate-pulse rounded-control bg-border" />
        <div className="h-4 w-56 animate-pulse rounded-control bg-border" />
      </div>
      <div className="rounded-card border border-border bg-surface px-4">
        <SkeletonRow />
        <div className="flex justify-between py-3"><div className="h-5 w-16 animate-pulse rounded-control bg-border" /><div className="h-5 w-20 animate-pulse rounded-control bg-border" /></div>
      </div>
      <div className="h-10 w-full animate-pulse rounded-control bg-border" />
    </div>
  );
}
