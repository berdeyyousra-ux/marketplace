import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date, label } from "@/lib/format";
import type { AdminRefundSummary, Page } from "@/lib/types";
import { StatusBadge } from "@/components/StatusBadge";

const statuses = ["Submitted", "Approved", "Executing", "Failed", "Completed", "Rejected"];

// EP-62 and US-30: oldest first, so the queue is worked in order.
export default async function RefundQueuePage({ searchParams }: { searchParams: Promise<{ status?: string; cursor?: string }> }) {
  const { status = "Submitted", cursor } = await searchParams;
  const query = new URLSearchParams({ limit: "25" });
  if (status !== "All") query.set("status", status);
  if (cursor) query.set("cursor", cursor);
  const queue = await serverGet<Page<AdminRefundSummary>>(`/admin/refund-requests?${query}`, `/admin/refunds?status=${status}`);
  if (!queue.ok) {
    return <p className="error">{queue.status === 403 ? "The refund queue is for admins." : "The refund queue could not be loaded."}</p>;
  }

  return (
    <div className="stack">
      <h1>Refund queue</h1>
      <div className="row">
        {[...statuses, "All"].map((value) => (
          <Link key={value} href={`/admin/refunds?status=${value}`} className={`button ${value === status ? "" : "secondary"}`}>{value}</Link>
        ))}
      </div>
      {queue.data.items.length === 0 ? (
        <p className="muted">Nothing in this list.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Requested</th><th>Product</th><th>Status</th><th>Policy</th><th>AI suggestion</th><th /></tr></thead>
            <tbody>
              {queue.data.items.map((request) => (
                <tr key={request.id}>
                  <td>{date(request.createdAt)}</td>
                  <td>{request.productTitle}<div className="small muted">{request.downloadCount} downloads</div></td>
                  <td><StatusBadge value={request.status} /></td>
                  <td><StatusBadge value={request.policyVerdict} /></td>
                  <td>
                    {request.ai?.recommendedAction ? (
                      <>
                        <StatusBadge value={request.ai.recommendedAction} /> <span className="small">{label(request.ai.category)}</span>
                        {request.ai.flags.length > 0 && <div className="small muted">{request.ai.flags.map(label).join(", ")}</div>}
                      </>
                    ) : (
                      <span className="small muted">{request.ai ? `No suggestion (${label(request.ai.status)})` : "Not analyzed yet"}</span>
                    )}
                  </td>
                  <td><Link href={`/admin/refunds/${request.id}`}>Review</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {queue.data.nextCursor && (
        <Link className="button secondary" href={`/admin/refunds?status=${status}&cursor=${encodeURIComponent(queue.data.nextCursor)}`}>More</Link>
      )}
    </div>
  );
}
