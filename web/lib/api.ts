import "server-only";
import { redirect } from "next/navigation";
import { API_BASE_URL, readTokens } from "./session";
import type { Me, Problem } from "./types";

export type ApiResult<T> = { ok: true; status: number; data: T; etag: string | null } | { ok: false; status: number; problem: Problem };

// Server-side GET for pages. A missing or expired access token sends the browser to /session/refresh, which renews
// the session through the browser's single-flight refresh and comes back to `returnTo`; with no session at all it goes
// to the login page. Servers never rotate tokens themselves (lib/refresh.ts explains why). For a missing access
// cookie, proxy.ts has usually redirected already, before the layout could start a refresh of its own.
export async function serverGet<T>(path: string, returnTo: string): Promise<ApiResult<T>> {
  const { access, refresh } = await readTokens();
  if (!access) {
    redirect(refresh ? refreshUrl(returnTo) : `/login?next=${encodeURIComponent(returnTo)}`);
  }

  const response = await fetch(`${API_BASE_URL}/api/v1${path}`, {
    headers: { Authorization: `Bearer ${access}` },
    cache: "no-store",
  });
  if (response.status === 401) {
    redirect(refresh ? refreshUrl(returnTo) : `/login?next=${encodeURIComponent(returnTo)}`);
  }

  return toResult<T>(response);
}

// GET for public pages. With authenticated, the signed-in caller's token is sent when there is one, so the answer can
// include viewer-specific fields (viewerOwns); it never redirects, and an expired token simply reads as anonymous.
export async function publicGet<T>(path: string, options: { authenticated?: boolean } = {}): Promise<ApiResult<T>> {
  const { access } = options.authenticated ? await readTokens() : { access: undefined };
  const response = await fetch(`${API_BASE_URL}/api/v1${path}`, {
    headers: access ? { Authorization: `Bearer ${access}` } : undefined,
    cache: "no-store",
  });
  return toResult<T>(response);
}

// The signed-in user, or null. Never redirects, so it is safe in the layout.
export async function currentUser(): Promise<Me | null> {
  return (await currentSession()).me;
}

// The user, and whether a session can be restored: no access cookie, but a refresh token. The layout then shows
// a neutral header and the browser restores the session (components/SessionKeeper.tsx), instead of "Sign in".
export async function currentSession(): Promise<{ me: Me | null; restorable: boolean }> {
  const { access, refresh } = await readTokens();
  if (!access) {
    return { me: null, restorable: Boolean(refresh) };
  }

  const response = await fetch(`${API_BASE_URL}/api/v1/me`, { headers: { Authorization: `Bearer ${access}` }, cache: "no-store" });
  if (response.ok) {
    return { me: (await response.json()) as Me, restorable: false };
  }

  // A token that is present but refused was revoked (sign-out elsewhere, password reset), not expired: the cookie
  // expires 30 seconds before its token. Its refresh token was revoked with it, so there is nothing to restore.
  return { me: null, restorable: false };
}

export async function toResult<T>(response: Response): Promise<ApiResult<T>> {
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  return response.ok
    ? { ok: true, status: response.status, data: body as T, etag: response.headers.get("ETag") }
    : { ok: false, status: response.status, problem: (body ?? { title: response.statusText }) as Problem };
}

function refreshUrl(returnTo: string) {
  return `/session/refresh?next=${encodeURIComponent(returnTo)}`;
}
