"use client";

import type { Problem } from "./types";

export type ClientResult<T> = { ok: true; data: T; etag: string | null } | { ok: false; status: number; problem: Problem };

// Browser calls go to this app's /api/proxy, which adds the access token from the httpOnly cookie.
// body may be FormData (uploads), sent as multipart; anything else is sent as JSON. ifMatch carries an ETag (US-13).
export async function call<T>(
  path: string,
  init: { method?: string; body?: unknown; idempotent?: boolean; ifMatch?: string | null } = {},
): Promise<ClientResult<T>> {
  const headers: Record<string, string> = {};
  const multipart = init.body instanceof FormData;
  if (init.body !== undefined && !multipart) {
    headers["Content-Type"] = "application/json";
  }

  if (init.ifMatch) {
    headers["If-Match"] = init.ifMatch;
  }

  if (init.idempotent) {
    headers["Idempotency-Key"] = crypto.randomUUID().replace(/-/g, "");
  }

  const response = await fetch(`/api/proxy${path}`, {
    method: init.method ?? "GET",
    headers,
    body: init.body === undefined ? undefined : multipart ? (init.body as FormData) : JSON.stringify(init.body),
  });
  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    // A non-JSON error page (a proxy timeout, say) still reads as a failure with a status.
  }
  if (response.status === 401) {
    window.location.href = `/login?next=${encodeURIComponent(window.location.pathname)}`;
  }

  return response.ok ? { ok: true, data: body as T, etag: response.headers.get("ETag") } : { ok: false, status: response.status, problem: (body ?? {}) as Problem };
}

// Field errors from a problem, keyed by lower-camel field name ("Email" and "email" both become "email").
export function fieldErrors(problem: Problem): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const [key, messages] of Object.entries(problem.errors ?? {})) {
    const name = key.charAt(0).toLowerCase() + key.slice(1);
    if (messages[0]) errors[name] = messages[0];
  }

  return errors;
}

// One readable line from an RFC 9457 problem, including the first field error.
export function describe(problem: Problem) {
  const field = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined;
  return field ?? problem.detail ?? problem.title ?? "Something went wrong.";
}
