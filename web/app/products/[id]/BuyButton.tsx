"use client";

import Link from "next/link";
import { useState } from "react";
import { call, describe } from "@/lib/client";
import type { OrderDetail } from "@/lib/types";
import { Button } from "@/components/ui/Button";

// EP-30: a one-item order at today's price, then checkout. The button stays busy until the page changes, so it
// cannot be pressed twice.
export function BuyButton({ productId, signedIn }: { productId: string; signedIn: boolean }) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!signedIn) {
    return (
      <Link href={`/login?next=/products/${productId}`} className="block rounded-control border border-accent bg-accent px-4 py-3 text-center type-label text-accent-contrast hover:bg-accent-hover">
        Sign in to buy
      </Link>
    );
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
    <div className="flex flex-col gap-2">
      <Button onClick={buy} loading={busy} fullWidth className="py-3">Buy now</Button>
      <p aria-live="polite" className="m-0 type-label text-danger">{error}</p>
    </div>
  );
}
