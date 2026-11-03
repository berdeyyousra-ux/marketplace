import { relayAnonymous } from "@/lib/relay";

// EP-06. Always 202, whether or not the email has an account (US-06).
export async function POST(request: Request) {
  return relayAnonymous(request, "/auth/forgot-password");
}
