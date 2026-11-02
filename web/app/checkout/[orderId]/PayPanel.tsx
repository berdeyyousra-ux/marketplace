"use client";

import { useState } from "react";
import { call, describe } from "@/lib/client";
import { date } from "@/lib/format";
import type { OrderDetail, PaymentStart } from "@/lib/types";

// EP-33 starts the payment. With the fake provider (development), the test payment is completed through the API's
// EP-99, which sends the same signed webhook a real provider would; the Worker then marks the order paid. Stripe's
// payment form arrives with T-064.
export function PayPanel({ orderId, expiresAt }: { orderId: string; expiresAt: string }) {
  const [payment, setPayment] = useState<PaymentStart | null>(null);
  const [state, setState] = useState<"idle" | "busy" | "waiting">("idle");
  const [error, setError] = useState<string | null>(null);

  async function start() {
    setState("busy");
    setError(null);
    const result = await call<PaymentStart>(`/orders/${orderId}/payment`, { method: "POST", idempotent: true });
    if (result.ok) {
      setPayment(result.data);
    } else {
      setError(describe(result.problem));
    }

    setState("idle");
  }

  async function completeTestPayment() {
    if (!payment) return;
    setState("waiting");
    setError(null);
    const completed = await fetch("/api/dev/complete-payment", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ clientSecret: payment.clientSecret }),
    });
    if (!completed.ok) {
      setError("The test payment could not be completed. Is the API running in Development with the fake provider?");
      setState("idle");
      return;
    }

    // The Worker processes the webhook through the outbox; wait for the order to turn Paid.
    for (let attempt = 0; attempt < 30; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, 1000));
      const order = await call<OrderDetail>(`/orders/${orderId}`);
      if (order.ok && order.data.status === "Paid") {
        window.location.reload();
        return;
      }
    }

    setError("The payment was sent but the order is not paid yet. Is the Worker running?");
    setState("idle");
  }

  return (
    <div className="card stack">
      <p className="small muted">Pay before {date(expiresAt)}, when the order expires.</p>
      {error && <p className="error">{error}</p>}
      {!payment ? (
        <button onClick={start} disabled={state !== "idle"}>{state === "busy" ? "Starting…" : "Pay now"}</button>
      ) : payment.provider === "Fake" ? (
        <>
          <p className="notice small">Development payment provider: no card is charged.</p>
          <button onClick={completeTestPayment} disabled={state !== "idle"}>
            {state === "waiting" ? "Waiting for confirmation…" : "Complete test payment"}
          </button>
        </>
      ) : (
        <p className="notice">Card payment with {payment.provider} is not wired into this client yet (T-064).</p>
      )}
    </div>
  );
}
