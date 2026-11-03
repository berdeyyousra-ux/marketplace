import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date, shortId } from "@/lib/format";
import type { OrderDetail } from "@/lib/types";
import { StatusBadge } from "@/components/ui/Badge";
import { Money } from "@/components/ui/Money";
import { Thumbnail } from "@/components/ui/Thumbnail";
import { FormAlert } from "@/components/FormAlert";
import { StatusPanel } from "@/components/AuthShell";
import { PayPanel } from "./PayPanel";

const closedMessages: Record<string, string> = {
  Refunded: "This order was refunded in full.",
  Expired: "This order expired before it was paid, so nothing was charged.",
  Cancelled: "This order was cancelled, so nothing was charged.",
};

// EP-32 and EP-33: a Pending order is paid here; any other order shows its items and payment state.
export default async function CheckoutPage({ params }: { params: Promise<{ orderId: string }> }) {
  const { orderId } = await params;
  const order = await serverGet<OrderDetail>(`/orders/${orderId}`, `/checkout/${orderId}`);
  if (!order.ok) {
    return (
      <div className="mx-auto w-full max-w-[480px] px-6 py-8">
        <FormAlert>{order.status === 404 ? "This order could not be found." : "This order could not be loaded. Refresh the page to try again."}</FormAlert>
      </div>
    );
  }

  const { data } = order;
  const pending = data.status === "Pending";
  return (
    <div className="mx-auto flex w-full max-w-[480px] flex-col gap-6 px-6 py-8">
      <header className="flex flex-col gap-2">
        <h1 className="type-page-title m-0 text-text">{pending ? "Checkout" : "Order"}</h1>
        <p className="m-0 flex flex-wrap items-center gap-2 type-label text-muted">
          <span className="font-mono">{shortId(data.id)}</span>
          <span aria-hidden="true">·</span>
          <span>{date(data.createdAt)}</span>
          <StatusBadge status={data.status} />
        </p>
      </header>

      <section aria-label="Order summary" className="rounded-card border border-border bg-surface">
        <ul className="m-0 list-none p-0">
          {data.items.map((item) => (
            <li key={item.id} className="flex items-center gap-4 border-b border-border px-4 py-3">
              <Thumbnail id={item.productId} title={item.title} url={item.coverImageUrl} />
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="type-card-title truncate text-text">{item.title}</span>
                {item.status === "Refunded" && data.status !== "Refunded" && <span className="pt-1"><StatusBadge status={item.status} /></span>}
              </span>
              <Money value={item.unitPrice} className="type-label text-text" />
            </li>
          ))}
        </ul>
        <div className="flex items-center justify-between px-4 py-3">
          <span className="type-card-title text-text">Total</span>
          <Money value={data.total} className="type-price text-text" />
        </div>
      </section>

      {pending ? (
        <PayPanel orderId={data.id} total={data.total} expiresAt={data.expiresAt} />
      ) : data.status === "Paid" || data.status === "PartiallyRefunded" ? (
        <StatusPanel tone="success">
          <p className="m-0">Paid {date(data.paidAt)}{data.payment ? ` with ${data.payment.provider === "Fake" ? "a test payment" : data.payment.provider}` : ""}.</p>
          <p className="m-0 type-label"><Link href="/library" className="text-accent hover:underline">Go to your library</Link> to download.</p>
        </StatusPanel>
      ) : (
        <StatusPanel tone="neutral">
          <p className="m-0">{closedMessages[data.status] ?? "This order can't be paid."}</p>
          <p className="m-0 type-label"><Link href="/" className="text-accent hover:underline">Browse products</Link> to start a new one.</p>
        </StatusPanel>
      )}
    </div>
  );
}
