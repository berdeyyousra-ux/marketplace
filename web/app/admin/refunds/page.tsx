import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date, label } from "@/lib/format";
import { aiActionLabel } from "@/lib/ai";
import type { AdminRefundSummary, Page } from "@/lib/types";
import { PageHeader } from "@/components/ui/PageHeader";
import { EmptyState } from "@/components/ui/EmptyState";
import { Badge, StatusBadge } from "@/components/ui/Badge";
import { Money } from "@/components/ui/Money";
import { Pagination } from "@/components/ui/Pagination";
import { FormAlert } from "@/components/FormAlert";
import { cn } from "@/lib/cn";

const statuses = [
  { value: "Submitted", label: "To decide" },
  { value: "Failed", label: "Failed" },
  { value: "Approved", label: "Approved" },
  { value: "Executing", label: "Executing" },
  { value: "Completed", label: "Completed" },
  { value: "Rejected", label: "Rejected" },
  { value: "All", label: "All" },
];

function AiCell({ ai }: { ai: AdminRefundSummary["ai"] }) {
  if (!ai) return <span className="type-label text-muted">Not analysed yet</span>;
  if (!ai.recommendedAction) return <span className="type-label text-muted">No suggestion</span>;
  return <Badge>{aiActionLabel(ai.recommendedAction)}</Badge>;
}

// EP-62 and US-30: oldest first, so the queue is worked in order.
export default async function RefundQueuePage({ searchParams }: { searchParams: Promise<{ status?: string; cursor?: string }> }) {
  const { status = "Submitted", cursor } = await searchParams;
  const query = new URLSearchParams({ limit: "25" });
  if (status !== "All") query.set("status", status);
  if (cursor) query.set("cursor", cursor);
  const queue = await serverGet<Page<AdminRefundSummary>>(`/admin/refund-requests?${query}`, `/admin/refunds?status=${status}`);

  return (
    <div className="container-page py-8">
      <PageHeader title="Refund requests" description="Oldest first. The AI suggestion is advice; every decision is yours." />
      <nav aria-label="Filter by status" className="-mt-2 mb-6 flex flex-wrap gap-2">
        {statuses.map((option) => (
          <Link
            key={option.value}
            href={`/admin/refunds?status=${option.value}`}
            aria-current={option.value === status ? "page" : undefined}
            className={cn(
              "rounded-full border px-3 py-1 type-label",
              option.value === status ? "border-accent bg-accent-muted text-accent-hover" : "border-border-strong bg-surface text-text hover:border-accent",
            )}
          >
            {option.label}
          </Link>
        ))}
      </nav>

      {!queue.ok ? (
        <FormAlert>{queue.status === 403 ? "The refund queue is for admins." : "The refund queue could not be loaded. Refresh the page to try again."}</FormAlert>
      ) : queue.data.items.length === 0 ? (
        <EmptyState message={status === "Submitted" ? "Nothing to decide. The queue is clear." : "No requests with this status."} />
      ) : (
        <>
          <div className="hidden overflow-hidden rounded-card border border-border bg-surface md:block">
            <table className="w-full border-collapse">
              <thead>
                <tr className="border-b border-border text-left">
                  <th scope="col" className="px-4 py-3 type-micro text-muted">Requested</th>
                  <th scope="col" className="px-4 py-3 type-micro text-muted">Buyer</th>
                  <th scope="col" className="px-4 py-3 type-micro text-muted">Product</th>
                  <th scope="col" className="px-4 py-3 text-right type-micro text-muted">Amount</th>
                  <th scope="col" className="px-4 py-3 type-micro text-muted">AI suggestion</th>
                  <th scope="col" className="px-4 py-3 type-micro text-muted">Status</th>
                </tr>
              </thead>
              <tbody>
                {queue.data.items.map((request) => (
                  <tr key={request.id} className="border-b border-border last:border-b-0 hover:bg-bg">
                    <td className="px-4 py-3 type-label whitespace-nowrap text-text">{date(request.createdAt)}</td>
                    <td className="px-4 py-3 type-label text-text">{request.buyerName ?? <span className="text-muted">Deleted account</span>}</td>
                    <td className="px-4 py-3 type-label">
                      <Link href={`/admin/refunds/${request.id}`} className="font-medium text-accent-strong hover:underline">{request.productTitle}</Link>
                      {request.policyVerdict !== "Eligible" && <span className="block text-muted">{label(request.policyVerdict)}</span>}
                    </td>
                    <td className="px-4 py-3 text-right type-label text-text"><Money value={request.amount} /></td>
                    <td className="px-4 py-3"><AiCell ai={request.ai} /></td>
                    <td className="px-4 py-3"><StatusBadge status={request.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <ul className="m-0 flex list-none flex-col gap-3 p-0 md:hidden">
            {queue.data.items.map((request) => (
              <li key={request.id}>
                <Link href={`/admin/refunds/${request.id}`} className="flex flex-col gap-2 rounded-card border border-border bg-surface p-4 hover:border-accent">
                  <span className="flex items-start justify-between gap-3">
                    <span className="type-card-title text-text">{request.productTitle}</span>
                    <Money value={request.amount} className="type-label text-text" />
                  </span>
                  <span className="type-label text-muted">{request.buyerName ?? "Deleted account"} · {date(request.createdAt)}</span>
                  <span className="flex flex-wrap items-center gap-2"><StatusBadge status={request.status} /><AiCell ai={request.ai} /></span>
                </Link>
              </li>
            ))}
          </ul>

          <Pagination
            nextHref={queue.data.nextCursor ? `/admin/refunds?status=${status}&cursor=${encodeURIComponent(queue.data.nextCursor)}` : null}
            firstHref={`/admin/refunds?status=${status}`}
            onFirstPage={!cursor}
          />
        </>
      )}
    </div>
  );
}
