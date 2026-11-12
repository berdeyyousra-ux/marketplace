"use client";

import { useMemo, useState } from "react";
import { Elements, PaymentElement, useElements, useStripe } from "@stripe/react-stripe-js";
import { money } from "@/lib/format";
import { stripeAppearance, stripeFor } from "@/lib/stripe";
import type { Money } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { FormAlert } from "@/components/FormAlert";

// T-064: Stripe's Payment Element, mounted on the client secret EP-33 returned. The card details are typed into
// Stripe's iframe and go straight to Stripe; this app never sees them, so it stays out of PCI scope.
//
// Confirming does not pay the order. Stripe sends payment_intent.succeeded to the API's webhook, the Worker turns
// that into a paid order, and onConfirmed waits for the order to turn Paid. That is why a card payment takes a
// moment to land, and why "the payment went through" is never announced from the browser's result alone.
export function StripeForm({
  clientSecret,
  publishableKey,
  total,
  onConfirmed,
}: {
  clientSecret: string;
  publishableKey: string;
  total: Money;
  onConfirmed: () => Promise<void>;
}) {
  // Both are fixed for the life of the panel: Elements rejects a changed stripe prop or client secret.
  const stripe = useMemo(() => stripeFor(publishableKey), [publishableKey]);
  const appearance = useMemo(() => stripeAppearance(), []);
  return (
    <Elements stripe={stripe} options={{ clientSecret, appearance }}>
      <CardFields total={total} onConfirmed={onConfirmed} />
    </Elements>
  );
}

function CardFields({ total, onConfirmed }: { total: Money; onConfirmed: () => Promise<void> }) {
  const stripe = useStripe();
  const elements = useElements();
  const [ready, setReady] = useState(false);
  const [paying, setPaying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!stripe || !elements) {
      return;
    }

    setPaying(true);
    setError(null);

    // redirect: "if_required" keeps a card payment on this page and only leaves for a method that needs a bank
    // page. Those come back to this same checkout, where the panel reads redirect_status and waits for the order.
    const result = await stripe.confirmPayment({
      elements,
      confirmParams: { return_url: window.location.href.split("?")[0] },
      redirect: "if_required",
    });

    if (result.error) {
      // card_declined, incomplete fields and the like: Stripe's message is the one to show, and the form stays up
      // so another card can be tried. The PaymentIntent is still live, so no new one is started.
      setError(result.error.message ?? "This payment could not be completed. Check your card details and try again.");
      setPaying(false);
      return;
    }

    // "processing" counts as confirmed: the webhook arrives when it settles. Only a status that cannot become paid
    // sends the buyer back to the form.
    if (result.paymentIntent.status !== "succeeded" && result.paymentIntent.status !== "processing") {
      setError("This payment was not completed. Try again, or use another card.");
      setPaying(false);
      return;
    }

    await onConfirmed();
    setPaying(false);
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      {error && <FormAlert>{error}</FormAlert>}
      <PaymentElement onReady={() => setReady(true)} />
      <Button type="submit" loading={paying} disabled={!ready} fullWidth>Pay {money(total)}</Button>
    </form>
  );
}
