import "server-only";
import { redirect } from "next/navigation";
import { API_BASE_URL, readTokens } from "./session";
import type { Me, Problem } from "./types";

export type ApiResult<T> = { ok: true; status: number; data: T } | { ok: false; status: number; problem: Problem };

// Server-side GET for pages. A missing or expired access token sends the browser through /api/session/refresh,
// which rotates the tokens and comes back to `returnTo`; with no session at all it goes to the login page.
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

// Anonymous GET, for the public catalogue.
export async function publicGet<T>(path: string): Promise<ApiResult<T>> {
  const response = await fetch(`${API_BASE_URL}/api/v1${path}`, { cache: "no-store" });
  return toResult<T>(response);
}

// The signed-in user, or null. Never redirects, so it is safe in the layout.
export async function currentUser(): Promise<Me | null> {
  const { access } = await readTokens();
  if (!access) {
    return null;
  }

  const response = await fetch(`${API_BASE_URL}/api/v1/me`, { headers: { Authorization: `Bearer ${access}` }, cache: "no-store" });
  return response.ok ? ((await response.json()) as Me) : null;
}

export async function toResult<T>(response: Response): Promise<ApiResult<T>> {
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  return response.ok
    ? { ok: true, status: response.status, data: body as T }
    : { ok: false, status: response.status, problem: (body ?? { title: response.statusText }) as Problem };
}

function refreshUrl(returnTo: string) {
  return `/api/session/refresh?next=${encodeURIComponent(returnTo)}`;
}
