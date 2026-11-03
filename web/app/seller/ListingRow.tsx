"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { call, describe } from "@/lib/client";
import { date } from "@/lib/format";
import type { Product, SellerProductSummary } from "@/lib/types";
import { StatusBadge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Money } from "@/components/ui/Money";
import { Thumbnail } from "@/components/ui/Thumbnail";
import { useToast } from "@/components/ui/Toast";

// Publishing changes the product's version, so the current ETag is read first (EP-22) and sent as If-Match (EP-26, EP-27).
export async function changeStatus(productId: string, publish: boolean) {
  const current = await call<Product>(`/products/${productId}`);
  if (!current.ok) return current;
  return call<Product>(`/products/${productId}/${publish ? "publish" : "unpublish"}`, { method: "POST", ifMatch: current.etag });
}

export function ListingRow({ product }: { product: SellerProductSummary }) {
  const router = useRouter();
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const canPublish = product.status === "Draft" || product.status === "Unpublished";
  const canUnpublish = product.status === "Published";

  async function toggle() {
    setBusy(true);
    const result = await changeStatus(product.id, canPublish);
    setBusy(false);
    if (!result.ok) {
      toast("error", describe(result.problem));
      return;
    }

    toast("success", canPublish ? `“${product.title}” is live.` : `“${product.title}” is hidden from the catalogue. Buyers keep their access.`);
    router.refresh();
  }

  return (
    <li className="flex flex-wrap items-center gap-4 border-b border-border px-4 py-3 last:border-b-0 sm:flex-nowrap">
      <Thumbnail id={product.id} title={product.title} url={product.coverImageUrl} />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <span className="flex flex-wrap items-center gap-2">
          <Link href={`/seller/products/${product.id}`} className="type-card-title truncate text-text hover:text-accent">{product.title}</Link>
          <StatusBadge status={product.status} />
        </span>
        <span className="type-label text-muted">
          <Money value={product.price} /> · {product.salesCount} {product.salesCount === 1 ? "sale" : "sales"} · updated {date(product.updatedAt)}
        </span>
      </div>
      <div className="flex w-full items-center justify-end gap-2 sm:w-auto">
        {canPublish && <Button variant="secondary" loading={busy} onClick={toggle}>Publish</Button>}
        {canUnpublish && <Button variant="ghost" loading={busy} onClick={toggle}>Unpublish</Button>}
        <Link href={`/seller/products/${product.id}`} className="rounded-control border border-border-strong px-4 py-2 type-label text-accent hover:border-accent">Edit</Link>
      </div>
    </li>
  );
}
