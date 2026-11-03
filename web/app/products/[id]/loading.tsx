import { SkeletonText } from "@/components/ui/Skeleton";

export default function Loading() {
  return (
    <div className="container-page grid gap-10 py-8 lg:grid-cols-2" aria-busy="true" aria-label="Loading product">
      <div className="aspect-[4/3] w-full animate-pulse rounded-card bg-border" />
      <div className="flex flex-col gap-6">
        <div className="h-10 w-3/4 animate-pulse rounded-control bg-border" />
        <div className="h-6 w-24 animate-pulse rounded-control bg-border" />
        <div className="h-12 w-full animate-pulse rounded-control bg-border" />
        <SkeletonText lines={4} />
      </div>
    </div>
  );
}
