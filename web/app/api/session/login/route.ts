import { NextResponse } from "next/server";
import { API_BASE_URL, writeTokens, type TokenPair } from "@/lib/session";

// EP-03 through this server, so the tokens land in httpOnly cookies and never reach browser code.
export async function POST(request: Request) {
  const body = await request.json();
  const response = await fetch(`${API_BASE_URL}/api/v1/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: body.email, password: body.password }),
    cache: "no-store",
  });
  if (!response.ok) {
    return new NextResponse(await response.text(), { status: response.status, headers: { "Content-Type": "application/problem+json" } });
  }

  const result = NextResponse.json({ ok: true });
  writeTokens(result.cookies, (await response.json()) as TokenPair);
  return result;
}
