"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { call, describe } from "@/lib/client";
import { date, money } from "@/lib/format";
import type { Money, OrderDetail, PaymentStart } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { FormAlert } from "@/components/FormAlert";
import { StripeForm } from "./StripeForm";

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// EP-33 starts the payment as soon as the panel opens, because which provider is configured decides what is shown:
// Stripe's payment form (T-064) or, with the fake provider in development, one "Pay" button. Starting it twice is
// safe either way: EP-33 reuses the order's live payment, so there is only ever one payment intent per order.
//
// Neither path marks the order paid from the browser. The provider's webhook reaches the API, the Worker turns it
// into a paid order, and this panel waits for that; a payment is only ever confirmed by the order's own status.
export function PayPanel({ orderId, total, expiresAt }: { orderId: string; total: Money; expiresAt: string }) {
  const router = useRouter();
  const [start, setStart] = useState<PaymentStart | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [waiting, setWaiting] = useState(false);
  // Set once the provider has taken the money. The form never comes back after that, even if the wait below runs
  // out, so a buyer whose order is slow to update cannot be invited to pay a second time.
  const [paid, setPaid] = useState(false);
  const started = useRef(false);

  // Waits for the Worker to process the provider's webhook. The order's status is the single source of truth.
  const waitForPaid = useCallback(async () => {
    setPaid(true);
    setWaiting(true);
    setError(null);
    for (let attempt = 0; attempt < 30; attempt++) {
      await sleep(1000);
      const order = await call<OrderDetail>(`/orders/${orderId}`);
      if (order.ok && order.data.status === "Paid") {
        router.push("/library?toast=paid");
        return;
      }
    }

    setWaiting(false);
    setError("Your payment was received but the order hasn't updated yet. Check your orders in a minute; you won't be charged twice.");
  }, [orderId, router]);

  useEffect(() => {
    // One start per mount, even under React's development double-render.
    if (started.current) {
      return;
    }

    started.current = true;

    void (async () => {
      const response = await call<PaymentStart>(`/orders/${orderId}/payment`, { method: "POST", idempotent: true });
      if (!response.ok) {
        setError(describe(response.problem));
        return;
      }

      setStart(response.data);

      // Coming back from a payment method that needed a bank page: the money is already confirmed, so wait for the
      // order instead of showing the form again.
      if (new URLSearchParams(window.location.search).get("redirect_status") === "succeeded") {
        void waitForPaid();
      }
    })();
  }, [orderId, waitForPaid]);

  if (error && (!start || paid)) {
    return <FormAlert>{error}</FormAlert>;
  }

  if (waiting) {
    return (
      <div className="flex flex-col gap-3">
        <Button loading fullWidth>Pay {money(total)}</Button>
        <Note aria-live="polite">Confirming your payment. Keep this page open.</Note>
      </div>
    );
  }

  if (!start) {
    return (
      <div className="flex flex-col gap-3">
        <Button loading fullWidth>Pay {money(total)}</Button>
        <Note>Preparing a secure payment.</Note>
      </div>
    );
  }

  if (start.provider === "Stripe") {
    if (!start.publishableKey) {
      // Startup validation requires the key wherever Stripe is the provider, so this is a misconfiguration.
      return <FormAlert>Card payment isn&apos;t available right now. Nothing has been charged; try again shortly.</FormAlert>;
    }

    return (
      <div className="flex flex-col gap-3">
        {error && <FormAlert>{error}</FormAlert>}
        <StripeForm
          clientSecret={start.clientSecret}
          publishableKey={start.publishableKey}
          total={total}
          onConfirmed={waitForPaid}
        />
        <Note>Reserved for you until {date(expiresAt)}.</Note>
      </div>
    );
  }

  if (start.provider === "Fake") {
    return <FakePayPanel clientSecret={start.clientSecret} total={total} expiresAt={expiresAt} onPaid={waitForPaid} />;
  }

  return <FormAlert>Card payment with {start.provider} isn&apos;t available in this client yet.</FormAlert>;
}

// Development only: the API's EP-99 sends the signed webhook a real provider would, so the rest of the flow (the
// inbox, the outbox, the Worker, the order turning Paid) is exercised exactly as it is with Stripe.
function FakePayPanel({
  clientSecret,
  total,
  expiresAt,
  onPaid,
}: {
  clientSecret: string;
  total: Money;
  expiresAt: string;
  onPaid: () => Promise<void>;
}) {
  const [paying, setPaying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function pay() {
    setPaying(true);
    setError(null);
    const completed = await fetch("/api/dev/complete-payment", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ clientSecret }),
    });
    if (!completed.ok) {
      setError("The test payment could not be completed. Is the API running in Development with the fake provider?");
      setPaying(false);
      return;
    }

    await onPaid();
    setPaying(false);
  }

  return (
    <div className="flex flex-col gap-3">
      {error && <FormAlert>{error}</FormAlert>}
      <Button onClick={pay} loading={paying} fullWidth>Complete test payment of {money(total)}</Button>
      <Note>Reserved for you until {date(expiresAt)}.</Note>
    </div>
  );
}

function Note({ children, ...rest }: React.HTMLAttributes<HTMLParagraphElement>) {
  return (
    <p {...rest} className="m-0 text-center type-label text-muted">{children}</p>
  );
}
