"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";
import { refreshSession } from "@/lib/refresh";

let lastAttempt = 0;

// Rendered when the access token is gone but a refresh token is present: restores the session in the background and
// re-renders the page with it, so a public page never shows a signed-in user as signed out. If the refresh fails the
// cookies are cleared and the re-render shows the signed-out state. At most one attempt every 30 seconds, so a
// failing API can't cause a refresh loop.
export function SessionKeeper({ restore }: { restore: boolean }) {
  const router = useRouter();
  // /session/refresh restores the session itself; a second start there would only add a rotation.
  const ownPage = usePathname() === "/session/refresh";

  useEffect(() => {
    if (!restore || ownPage || Date.now() - lastAttempt < 30_000) return;
    lastAttempt = Date.now();
    refreshSession().then(() => router.refresh());
  }, [restore, ownPage, router]);

  return null;
}
