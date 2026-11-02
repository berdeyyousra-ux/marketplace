"use client";

import type { Problem } from "./types";

export type ClientResult<T> = { ok: true; data: T } | { ok: false; status: number; problem: Problem };

// Browser calls go to this app's /api/proxy, which adds the access token from the httpOnly cookie.
export async function call<T>(path: string, init: { method?: string; body?: unknown; idempotent?: boolean } = {}): Promise<ClientResult<T>> {
  const headers: Record<string, string> = {};
  if (init.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  if (init.idempotent) {
    headers["Idempotency-Key"] = crypto.randomUUID().replace(/-/g, "");
  }

  const response = await fetch(`/api/proxy${path}`, {
    method: init.method ?? "GET",
    headers,
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  });
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  if (response.status === 401) {
    window.location.href = `/login?next=${encodeURIComponent(window.location.pathname)}`;
  }

  return response.ok ? { ok: true, data: body as T } : { ok: false, status: response.status, problem: (body ?? {}) as Problem };
}

// One readable line from an RFC 9457 problem, including the first field error.
export function describe(problem: Problem) {
  const field = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined;
  return field ?? problem.detail ?? problem.title ?? "Something went wrong.";
}
