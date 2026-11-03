"use client";

import { useEffect } from "react";
import { refreshSession } from "@/lib/refresh";
import { SkeletonText } from "@/components/ui/Skeleton";

// Signed-in pages send the browser here when the access token has expired; it comes back once the session is renewed.
export function Restore({ next }: { next: string }) {
  useEffect(() => {
    refreshSession().then((ok) => window.location.replace(ok ? next : `/login?next=${encodeURIComponent(next)}`));
  }, [next]);

  return (
    <div role="status" aria-label="Restoring your session" className="mx-auto flex w-full max-w-[448px] flex-col gap-3 px-6 py-8">
      <p className="m-0 type-body text-muted">Restoring your session…</p>
      <SkeletonText lines={2} />
    </div>
  );
}
