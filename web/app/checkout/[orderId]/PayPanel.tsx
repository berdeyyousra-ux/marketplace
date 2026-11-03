"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { call, describe } from "@/lib/client";
import { date, money } from "@/lib/format";
import type { Money, OrderDetail, PaymentStart } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { FormAlert } from "@/components/FormAlert";

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// One Pay button that stays in its loading state from the first click until the order is paid, so it can't be pressed
// twice. EP-33 starts the payment; with the fake provider (development) the test payment is completed through the
// API's EP-99, which sends the signed webhook a real provider would, and the Worker marks the order paid. Stripe's
// payment form arrives with T-064.
export function PayPanel({ orderId, total, expiresAt }: { orderId: string; total: Money; expiresAt: string }) {
  const router = useRouter();
  const [paying, setPaying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [unsupported, setUnsupported] = useState<string | null>(null);

  async function pay() {
    setPaying(true);
    setError(null);
    const started = await call<PaymentStart>(`/orders/${orderId}/payment`, { method: "POST", idempotent: true });
    if (!started.ok) {
      setError(describe(started.problem));
      setPaying(false);
      return;
    }

    if (started.data.provider !== "Fake") {
      setUnsupported(started.data.provider);
      setPaying(false);
      return;
    }

    const completed = await fetch("/api/dev/complete-payment", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ clientSecret: started.data.clientSecret }),
    });
    if (!completed.ok) {
      setError("The test payment could not be completed. Is the API running in Development with the fake provider?");
      setPaying(false);
      return;
    }

    // The Worker handles the webhook through the outbox; wait for the order to turn Paid.
    for (let attempt = 0; attempt < 30; attempt++) {
      await sleep(1000);
      const order = await call<OrderDetail>(`/orders/${orderId}`);
      if (order.ok && order.data.status === "Paid") {
        router.push("/library?toast=paid");
        return;
      }
    }

    setError("Your payment was received but the order hasn't updated yet. Check your orders in a minute; you won't be charged twice.");
    setPaying(false);
  }

  if (unsupported) {
    return <FormAlert>Card payment with {unsupported} isn&apos;t available in this client yet.</FormAlert>;
  }

  return (
    <div className="flex flex-col gap-3">
      {error && <FormAlert>{error}</FormAlert>}
      <Button onClick={pay} loading={paying} fullWidth>Pay {money(total)}</Button>
      <p className="m-0 text-center type-label text-muted" aria-live="polite">
        {paying ? "Confirming your payment. Keep this page open." : `Reserved for you until ${date(expiresAt)}.`}
      </p>
    </div>
  );
}
