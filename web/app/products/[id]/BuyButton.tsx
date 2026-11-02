"use client";

import { useState } from "react";
import { call, describe } from "@/lib/client";
import type { OrderDetail } from "@/lib/types";

// EP-30: one-item order at today's price, then on to checkout.
export function BuyButton({ productId, signedIn }: { productId: string; signedIn: boolean }) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!signedIn) {
    return <a className="button" href={`/login?next=/products/${productId}`}>Sign in to buy</a>;
  }

  async function buy() {
    setBusy(true);
    setError(null);
    const result = await call<OrderDetail>("/orders", { method: "POST", body: { productIds: [productId] }, idempotent: true });
    if (result.ok) {
      window.location.href = `/checkout/${result.data.id}`;
      return;
    }

    setError(describe(result.problem));
    setBusy(false);
  }

  return (
    <div className="stack">
      {error && <p className="error">{error}</p>}
      <button onClick={buy} disabled={busy}>{busy ? "Creating order…" : "Buy now"}</button>
    </div>
  );
}
