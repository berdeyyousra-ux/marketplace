"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { StatusPanel } from "@/components/AuthShell";
import { SkeletonText } from "@/components/ui/Skeleton";

// EP-02 runs once on arrival; a token works only once, so a double effect run must not send it twice.
export function Verify({ token }: { token: string | null }) {
  const [state, setState] = useState<"working" | "failed">(token ? "working" : "failed");
  const sent = useRef(false);

  useEffect(() => {
    if (!token || sent.current) return;
    sent.current = true;
    fetch("/api/session/verify", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ token }) })
      .then((response) => (response.ok ? window.location.replace("/login?verified=1") : setState("failed")))
      .catch(() => setState("failed"));
  }, [token]);

  if (state === "working") {
    return (
      <div role="status" aria-label="Confirming your email" className="flex flex-col gap-3">
        <p className="m-0 type-body text-muted">Confirming your email…</p>
        <SkeletonText lines={2} />
      </div>
    );
  }

  return (
    <StatusPanel tone="danger">
      <p className="m-0 font-medium">This link can&apos;t be used</p>
      <p className="m-0">It may have expired (links last 24 hours), been used already, or been copied incompletely.</p>
      <p className="m-0 type-label">
        Already confirmed? <Link href="/login" className="text-accent hover:underline">Sign in</Link>. Otherwise{" "}
        <Link href="/register" className="text-accent hover:underline">register again</Link> to get a new link.
      </p>
    </StatusPanel>
  );
}
