export default function Loading() {
  return (
    <div className="container-page py-8" aria-busy="true" aria-label="Loading your orders">
      <div className="mb-8 h-10 w-36 animate-pulse rounded-control bg-border" />
      <div className="flex flex-col rounded-card border border-border bg-surface px-4">
        {Array.from({ length: 5 }, (_, index) => (
          <div key={index} className="flex items-center gap-6 border-b border-border py-4 last:border-b-0" aria-hidden="true">
            <div className="h-4 w-20 animate-pulse rounded-control bg-border" />
            <div className="h-4 flex-1 animate-pulse rounded-control bg-border" />
            <div className="h-6 w-20 animate-pulse rounded-full bg-border" />
          </div>
        ))}
      </div>
    </div>
  );
}
