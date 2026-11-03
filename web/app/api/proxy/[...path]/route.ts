import { NextResponse, type NextRequest } from "next/server";
import { API_BASE_URL, readTokens } from "@/lib/session";

// Browser calls to /api/proxy/<path> go to the API's /api/v1/<path> with the access token from the cookie. A 401 is
// passed back unchanged: the browser renews the session through its single-flight refresh (lib/refresh.ts) and
// retries. Rotating here would let parallel calls send the same refresh token, which the API treats as reuse.
const FORWARDED_REQUEST_HEADERS = ["content-type", "idempotency-key", "if-match"];
const FORWARDED_RESPONSE_HEADERS = ["content-type", "etag", "location", "retry-after", "idempotency-replayed"];

async function forward(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const { path } = await context.params;
  const target = `${API_BASE_URL}/api/v1/${path.map(encodeURIComponent).join("/")}${request.nextUrl.search}`;
  const body = request.method === "GET" || request.method === "HEAD" ? undefined : await request.arrayBuffer();
  const { access } = await readTokens();

  const send = (token: string | undefined) => {
    const headers = new Headers();
    for (const name of FORWARDED_REQUEST_HEADERS) {
      const value = request.headers.get(name);
      if (value) {
        headers.set(name, value);
      }
    }

    if (token) {
      headers.set("Authorization", `Bearer ${token}`);
    }

    return fetch(target, { method: request.method, headers, body, cache: "no-store" });
  };

  const response = await send(access);

  const result = new NextResponse(response.status === 204 ? null : await response.arrayBuffer(), { status: response.status });
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = response.headers.get(name);
    if (value) {
      result.headers.set(name, value);
    }
  }

  return result;
}

export { forward as GET, forward as POST, forward as PUT, forward as DELETE };
