import { NextResponse } from "next/server";
import { clearTokens, readTokens, refreshTokens, writeTokens } from "@/lib/session";

// EP-04 for the browser. Called only through lib/refresh.ts, which serializes calls across tabs; a failed refresh
// clears the cookies so the app shows the signed-out state.
export async function POST() {
  const { refresh } = await readTokens();
  const tokens = refresh ? await refreshTokens(refresh) : null;
  const result = NextResponse.json({ ok: tokens !== null }, { status: tokens ? 200 : 401 });
  if (tokens) {
    writeTokens(result.cookies, tokens);
  } else {
    clearTokens(result.cookies);
  }

  return result;
}
