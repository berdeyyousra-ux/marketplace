import { SkeletonCard } from "@/components/ui/Skeleton";

// The catalogue's shape while it loads, so nothing shifts when products arrive.
export default function Loading() {
  return (
    <div className="flex flex-col" aria-busy="true" aria-label="Loading products">
      <div className="mb-8 h-10 w-48 animate-pulse rounded-control bg-border" />
      <div className="flex gap-8">
        <div className="hidden w-60 shrink-0 lg:block" />
        <div className="grid flex-1 grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {Array.from({ length: 8 }, (_, index) => <SkeletonCard key={index} />)}
        </div>
      </div>
    </div>
  );
}
