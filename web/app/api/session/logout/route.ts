import { NextResponse } from "next/server";
import { API_BASE_URL, clearTokens, readTokens } from "@/lib/session";

// EP-05: revokes the refresh token's family, then clears the cookies.
export async function POST() {
  const { refresh } = await readTokens();
  if (refresh) {
    await fetch(`${API_BASE_URL}/api/v1/auth/logout`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: refresh }),
      cache: "no-store",
    });
  }

  const result = NextResponse.json({ ok: true });
  clearTokens(result.cookies);
  return result;
}
