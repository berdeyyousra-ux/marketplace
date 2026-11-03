import { SkeletonRow } from "@/components/ui/Skeleton";

export default function Loading() {
  return (
    <div className="container-page py-8" aria-busy="true" aria-label="Loading your library">
      <div className="mb-8 flex flex-col gap-2">
        <div className="h-10 w-40 animate-pulse rounded-control bg-border" />
        <div className="h-5 w-96 max-w-full animate-pulse rounded-control bg-border" />
      </div>
      <div className="rounded-card border border-border bg-surface px-4">
        {Array.from({ length: 4 }, (_, index) => <SkeletonRow key={index} />)}
      </div>
    </div>
  );
}
