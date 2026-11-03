import "server-only";
import { NextResponse } from "next/server";
import { API_BASE_URL } from "./session";

// Forwards an anonymous auth call to the API and relays its answer. A 202 or 204 may carry no body, and a Response
// with a null-body status cannot be given one.
export async function relayAnonymous(request: Request, apiPath: string) {
  const response = await fetch(`${API_BASE_URL}/api/v1${apiPath}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: await request.text(),
    cache: "no-store",
  });
  const body = response.status === 204 ? null : await response.text();
  return new NextResponse(body || null, {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}
