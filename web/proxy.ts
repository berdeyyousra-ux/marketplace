import { NextResponse, type NextRequest } from "next/server";

// Signed-in pages without an access token go straight to /session/refresh, before any HTML is rendered. Rendering
// first would let the layout start a background refresh that the page's own redirect then aborts mid-flight: the API
// may already have rotated the token while the browser never stores the new one, so the next refresh sends a used
// token and the API revokes the whole session (EP-04 has no reuse grace).
//
// This only reads cookies. It never rotates tokens and keeps no state (Next runs proxy apart from the app), and the
// matcher leaves out /api so request bodies, such as 100 MB uploads, are not buffered here.
export function proxy(request: NextRequest) {
  if (request.cookies.has("mp_at") || !request.cookies.has("mp_rt")) {
    return NextResponse.next();
  }

  const next = `${request.nextUrl.pathname}${request.nextUrl.search}`;
  return NextResponse.redirect(new URL(`/session/refresh?next=${encodeURIComponent(next)}`, request.url));
}

export const config = {
  matcher: ["/library", "/orders", "/checkout/:path*", "/seller", "/seller/:path*", "/admin/:path*"],
};
