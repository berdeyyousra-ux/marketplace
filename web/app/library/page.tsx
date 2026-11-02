import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date } from "@/lib/format";
import type { LibraryItem, MyRefundRequest, Page } from "@/lib/types";
import { StatusBadge } from "@/components/StatusBadge";
import { LibraryActions } from "./LibraryActions";

// EP-51 with EP-50 downloads, plus the buyer's refund requests (EP-60, EP-61).
export default async function LibraryPage() {
  const [library, refunds] = await Promise.all([
    serverGet<Page<LibraryItem>>("/library?limit=100", "/library"),
    serverGet<Page<MyRefundRequest>>("/refund-requests/mine?limit=100", "/library"),
  ]);
  if (!library.ok) {
    return <p className="error">Your library could not be loaded.</p>;
  }

  const requested = new Set((refunds.ok ? refunds.data.items : []).map((request) => request.orderItemId));
  return (
    <div className="stack">
      <h1>Library</h1>
      {library.data.items.length === 0 ? (
        <p className="muted">Nothing here yet. Purchases appear once they are paid. <Link href="/">Browse products</Link>.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Product</th><th>Purchased</th><th>Downloads</th><th>Status</th><th /></tr></thead>
            <tbody>
              {library.data.items.map((item) => (
                <tr key={item.orderItemId}>
                  <td>{item.title}<div className="small muted">{item.fileName}</div></td>
                  <td>{date(item.purchasedAt)}</td>
                  <td>{item.downloadCount} of {item.maxDownloads}</td>
                  <td><StatusBadge value={item.status} /></td>
                  <td><LibraryActions orderItemId={item.orderItemId} canDownload={item.status === "Active"} canRequestRefund={!requested.has(item.orderItemId) && item.status !== "Revoked"} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <h2>Refund requests</h2>
      {!refunds.ok || refunds.data.items.length === 0 ? (
        <p className="muted">No refund requests.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Product</th><th>Requested</th><th>Status</th><th>Note from the team</th></tr></thead>
            <tbody>
              {refunds.data.items.map((request) => (
                <tr key={request.id}>
                  <td>{request.productTitle}</td>
                  <td>{date(request.createdAt)}</td>
                  <td><StatusBadge value={request.status} /></td>
                  <td>{request.decisionNote ?? <span className="muted">–</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
