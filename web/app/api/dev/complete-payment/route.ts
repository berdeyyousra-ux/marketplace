import { NextResponse } from "next/server";
import { API_BASE_URL } from "@/lib/session";

// Completes a fake payment through the API's EP-99, standing in for the provider's checkout. EP-99 itself answers
// 404 unless the API runs in Development with the fake provider, so this route can do nothing anywhere else.
// The fake provider's client secret is "<providerPaymentId>_secret_fake". Stripe checkout arrives with T-064.
export async function POST(request: Request) {
  const { clientSecret } = (await request.json()) as { clientSecret?: string };
  const providerPaymentId = clientSecret?.endsWith("_secret_fake") ? clientSecret.slice(0, -"_secret_fake".length) : null;
  if (!providerPaymentId || !/^pi_fake_[0-9a-f]+$/.test(providerPaymentId)) {
    return NextResponse.json({ title: "Not a fake payment" }, { status: 400 });
  }

  const response = await fetch(`${API_BASE_URL}/dev/fake-payments/${providerPaymentId}/complete`, { method: "POST", cache: "no-store" });
  return new NextResponse(null, { status: response.status });
}
