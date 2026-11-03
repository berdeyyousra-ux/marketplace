import Link from "next/link";
import { Suspense } from "react";
import { serverGet } from "@/lib/api";
import type { LibraryItem, MyRefundRequest, Page } from "@/lib/types";
import { PageHeader } from "@/components/ui/PageHeader";
import { EmptyState } from "@/components/ui/EmptyState";
import { FormAlert } from "@/components/FormAlert";
import { ToastOnArrival } from "@/components/ToastOnArrival";
import { LibraryRow } from "./LibraryRow";

// EP-51 with EP-50 downloads, plus the buyer's refund requests (EP-60, EP-61) to mark the rows that have one.
export default async function LibraryPage() {
  const [library, refunds] = await Promise.all([
    serverGet<Page<LibraryItem>>("/library?limit=100", "/library"),
    serverGet<Page<MyRefundRequest>>("/refund-requests/mine?limit=100", "/library"),
  ]);

  // The newest request per item decides the row's badge; requests come newest first.
  const latestRequest = new Map<string, MyRefundRequest>();
  for (const request of refunds.ok ? refunds.data.items : []) {
    if (!latestRequest.has(request.orderItemId)) latestRequest.set(request.orderItemId, request);
  }

  return (
    <div className="container-page py-8">
      <Suspense><ToastOnArrival /></Suspense>
      <PageHeader title="Library" description="Everything you've bought. Download links are created when you click and last five minutes." />
      {!library.ok ? (
        <FormAlert>Your library could not be loaded. Refresh the page to try again.</FormAlert>
      ) : library.data.items.length === 0 ? (
        <EmptyState
          message="Nothing here yet. Products you buy appear here once they're paid."
          action={<Link href="/" className="rounded-control bg-accent px-4 py-2 type-label text-accent-contrast hover:bg-accent-hover">Browse products</Link>}
        />
      ) : (
        <ul className="m-0 flex list-none flex-col rounded-card border border-border bg-surface p-0">
          {library.data.items.map((item) => (
            <LibraryRow key={item.orderItemId} item={item} refund={latestRequest.get(item.orderItemId) ?? null} />
          ))}
        </ul>
      )}
    </div>
  );
}
