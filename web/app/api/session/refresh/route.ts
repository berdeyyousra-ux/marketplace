import { NextResponse, type NextRequest } from "next/server";
import { clearTokens, readTokens, refreshTokens, writeTokens } from "@/lib/session";

// Pages land here when the access token has expired: rotate the tokens and go back, or go to the login page.
export async function GET(request: NextRequest) {
  const next = safeNext(request.nextUrl.searchParams.get("next"));
  const { refresh } = await readTokens();
  const tokens = refresh ? await refreshTokens(refresh) : null;
  if (!tokens) {
    const result = NextResponse.redirect(new URL(`/login?next=${encodeURIComponent(next)}`, request.url));
    clearTokens(result.cookies);
    return result;
  }

  const result = NextResponse.redirect(new URL(next, request.url));
  writeTokens(result.cookies, tokens);
  return result;
}

// Also called by browser code after a change that alters the caller's roles (becoming a seller).
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

// Only same-site paths, so the redirect cannot be pointed elsewhere.
function safeNext(value: string | null) {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : "/";
}
