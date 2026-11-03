import { relayAnonymous } from "@/lib/relay";

// EP-02 for the link in the verification email.
export async function POST(request: Request) {
  return relayAnonymous(request, "/auth/verify-email");
}
