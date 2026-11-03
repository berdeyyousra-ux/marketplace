"use client";

// The only way this app rotates tokens (section 11.2). The API has no reuse grace (EP-04): two refreshes sent with the
// same refresh token revoke the whole session family. So refreshes are serialized here:
//   - within a tab, concurrent callers share one in-flight promise;
//   - across tabs and frames, a Web Lock lets one refresh run at a time. Each waits for the previous one's response,
//     whose Set-Cookie has already replaced the refresh cookie, so every request sends the newest token.
// Servers never rotate tokens: pages and the API proxy hand a 401 back to the browser, which comes through here.
let inFlight: Promise<boolean> | null = null;

const LOCK = "marketplace-session-refresh";

async function post() {
  try {
    const response = await fetch("/api/session/refresh", { method: "POST", cache: "no-store" });
    return response.ok;
  } catch {
    return false;
  }
}

export function refreshSession(): Promise<boolean> {
  inFlight ??= (typeof navigator !== "undefined" && navigator.locks ? navigator.locks.request(LOCK, post) : post()).finally(() => {
    inFlight = null;
  });
  return inFlight;
}
