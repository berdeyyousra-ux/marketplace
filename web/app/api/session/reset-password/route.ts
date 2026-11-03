import { relayAnonymous } from "@/lib/relay";

// EP-07. 204 when the password is set; every session is signed out (US-07).
export async function POST(request: Request) {
  return relayAnonymous(request, "/auth/reset-password");
}
