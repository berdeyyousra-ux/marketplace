import { serverGet } from "@/lib/api";
import { date, money } from "@/lib/format";
import type { OrderDetail } from "@/lib/types";
import { StatusBadge } from "@/components/StatusBadge";
import { PayPanel } from "./PayPanel";

// EP-32 and EP-33: the order, and payment while it is Pending.
export default async function CheckoutPage({ params }: { params: Promise<{ orderId: string }> }) {
  const { orderId } = await params;
  const order = await serverGet<OrderDetail>(`/orders/${orderId}`, `/checkout/${orderId}`);
  if (!order.ok) {
    return <p className="error">This order could not be found.</p>;
  }

  const { data } = order;
  return (
    <div className="stack" style={{ maxWidth: 720 }}>
      <h1>Checkout</h1>
      <div className="row">
        <StatusBadge value={data.status} />
        <span className="muted small">Order created {date(data.createdAt)}</span>
      </div>
      <div className="table-wrap">
        <table>
          <thead><tr><th>Product</th><th className="num">Price</th></tr></thead>
          <tbody>
            {data.items.map((item) => (
              <tr key={item.id}><td>{item.title}</td><td className="num">{money(item.unitPrice)}</td></tr>
            ))}
            <tr><td><strong>Total</strong></td><td className="num"><strong>{money(data.total)}</strong></td></tr>
          </tbody>
        </table>
      </div>
      {data.status === "Pending" ? (
        <PayPanel orderId={data.id} expiresAt={data.expiresAt} />
      ) : data.status === "Paid" ? (
        <p className="notice">Paid {date(data.paidAt)}. Your downloads are in the <a href="/library">library</a>.</p>
      ) : (
        <p className="muted">This order is {data.status.toLowerCase()} and cannot be paid.</p>
      )}
    </div>
  );
}
