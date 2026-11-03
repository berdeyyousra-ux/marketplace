import { relayAnonymous } from "@/lib/relay";

// EP-01. The API answers 202 whether or not the email is new, and sends the verification email.
export async function POST(request: Request) {
  return relayAnonymous(request, "/auth/register");
}
