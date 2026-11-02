import { NextResponse } from "next/server";
import { API_BASE_URL } from "@/lib/session";

// EP-07 for the link in the verification email.
export async function POST(request: Request) {
  const response = await fetch(`${API_BASE_URL}/api/v1/auth/verify-email`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: await request.text(),
    cache: "no-store",
  });
  // A 204 or 202 may carry no body, and a Response with a null-body status cannot be given one.
  const body = response.status === 204 ? null : await response.text();
  return new NextResponse(body || null, {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}
