import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date, money } from "@/lib/format";
import type { OrderSummary, Page } from "@/lib/types";
import { StatusBadge } from "@/components/StatusBadge";

// EP-31.
export default async function OrdersPage({ searchParams }: { searchParams: Promise<{ cursor?: string }> }) {
  const { cursor } = await searchParams;
  const orders = await serverGet<Page<OrderSummary>>(`/orders?limit=20${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`, "/orders");
  if (!orders.ok) {
    return <p className="error">Your orders could not be loaded.</p>;
  }

  return (
    <div className="stack">
      <h1>Orders</h1>
      {orders.data.items.length === 0 ? (
        <p className="muted">No orders yet. <Link href="/">Browse products</Link>.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Created</th><th>Status</th><th className="num">Items</th><th className="num">Total</th><th /></tr></thead>
            <tbody>
              {orders.data.items.map((order) => (
                <tr key={order.id}>
                  <td>{date(order.createdAt)}</td>
                  <td><StatusBadge value={order.status} /></td>
                  <td className="num">{order.itemCount}</td>
                  <td className="num">{money(order.total)}</td>
                  <td><Link href={`/checkout/${order.id}`}>{order.status === "Pending" ? "Pay" : "View"}</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {orders.data.nextCursor && <Link className="button secondary" href={`/orders?cursor=${encodeURIComponent(orders.data.nextCursor)}`}>Older orders</Link>}
    </div>
  );
}
