import "server-only";
import { cookies } from "next/headers";

// Section 11.2: tokens live only in httpOnly cookies set by this server; browser code never sees them.
export const ACCESS_COOKIE = "mp_at";
export const REFRESH_COOKIE = "mp_rt";
const REFRESH_MAX_AGE = 14 * 24 * 60 * 60;

export const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:8080";

export type TokenPair = { accessToken: string; expiresInSeconds: number; refreshToken: string; tokenType: string };

type CookieWriter = { set: (name: string, value: string, options: Record<string, unknown>) => void };

const base = { httpOnly: true, sameSite: "lax" as const, path: "/", secure: process.env.NODE_ENV === "production" };

export function writeTokens(target: CookieWriter, tokens: TokenPair) {
  target.set(ACCESS_COOKIE, tokens.accessToken, { ...base, maxAge: tokens.expiresInSeconds });
  target.set(REFRESH_COOKIE, tokens.refreshToken, { ...base, maxAge: REFRESH_MAX_AGE });
}

export function clearTokens(target: CookieWriter) {
  target.set(ACCESS_COOKIE, "", { ...base, maxAge: 0 });
  target.set(REFRESH_COOKIE, "", { ...base, maxAge: 0 });
}

export async function readTokens() {
  const store = await cookies();
  return { access: store.get(ACCESS_COOKIE)?.value, refresh: store.get(REFRESH_COOKIE)?.value };
}

// Rotates the refresh token (EP-04). Returns null when the session is over.
export async function refreshTokens(refreshToken: string): Promise<TokenPair | null> {
  const response = await fetch(`${API_BASE_URL}/api/v1/auth/refresh`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
    cache: "no-store",
  });
  return response.ok ? ((await response.json()) as TokenPair) : null;
}
