import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date, shortId } from "@/lib/format";
import type { OrderSummary, Page } from "@/lib/types";
import { PageHeader } from "@/components/ui/PageHeader";
import { EmptyState } from "@/components/ui/EmptyState";
import { StatusBadge } from "@/components/ui/Badge";
import { Money } from "@/components/ui/Money";
import { Pagination } from "@/components/ui/Pagination";
import { FormAlert } from "@/components/FormAlert";

function actionLabel(status: string) {
  return status === "Pending" ? "Pay" : "View";
}

// EP-31, newest first. A table from 768px; stacked cards below.
export default async function OrdersPage({ searchParams }: { searchParams: Promise<{ cursor?: string }> }) {
  const { cursor } = await searchParams;
  const orders = await serverGet<Page<OrderSummary>>(`/orders?limit=20${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`, "/orders");

  return (
    <div className="container-page py-8">
      <PageHeader title="Orders" />
      {!orders.ok ? (
        <FormAlert>Your orders could not be loaded. Refresh the page to try again.</FormAlert>
      ) : orders.data.items.length === 0 ? (
        <EmptyState
          message="No orders yet."
          action={<Link href="/" className="rounded-control bg-cta px-4 py-2 type-label text-cta-contrast hover:bg-cta-hover">Browse products</Link>}
        />
      ) : (
        <>
          <div className="hidden overflow-hidden rounded-card border border-border bg-surface md:block">
            <table className="w-full border-collapse">
              <thead>
                <tr className="border-b border-border text-left">
                  <th scope="col" className="px-4 py-3 type-micro text-muted">Order</th>
                  <th scope="col" className="px-4 py-3 type-micro text-muted">Date</th>
                  <th scope="col" className="px-4 py-3 text-right type-micro text-muted">Items</th>
                  <th scope="col" className="px-4 py-3 text-right type-micro text-muted">Total</th>
                  <th scope="col" className="px-4 py-3 type-micro text-muted">Status</th>
                  <th scope="col" className="px-4 py-3"><span className="sr-only">Details</span></th>
                </tr>
              </thead>
              <tbody>
                {orders.data.items.map((order) => (
                  <tr key={order.id} className="border-b border-border last:border-b-0 hover:bg-bg">
                    <td className="px-4 py-3 font-mono type-label text-text">{shortId(order.id)}</td>
                    <td className="px-4 py-3 type-label text-text">{date(order.createdAt)}</td>
                    <td className="px-4 py-3 text-right type-label tabular-nums text-text">{order.itemCount}</td>
                    <td className="px-4 py-3 text-right type-label text-text"><Money value={order.total} /></td>
                    <td className="px-4 py-3"><StatusBadge status={order.status} /></td>
                    <td className="px-4 py-3 text-right">
                      <Link href={`/checkout/${order.id}`} className="type-label text-accent-strong hover:underline" aria-label={`${actionLabel(order.status)} order ${shortId(order.id)}`}>
                        {actionLabel(order.status)}
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <ul className="m-0 flex list-none flex-col gap-3 p-0 md:hidden">
            {orders.data.items.map((order) => (
              <li key={order.id}>
                <Link href={`/checkout/${order.id}`} className="flex flex-col gap-2 rounded-card border border-border bg-surface p-4 hover:border-accent">
                  <span className="flex items-center justify-between gap-3">
                    <span className="font-mono type-label text-text">{shortId(order.id)}</span>
                    <StatusBadge status={order.status} />
                  </span>
                  <span className="flex items-center justify-between gap-3 type-label text-muted">
                    <span>{date(order.createdAt)} · {order.itemCount} {order.itemCount === 1 ? "item" : "items"}</span>
                    <Money value={order.total} className="type-price text-text" />
                  </span>
                </Link>
              </li>
            ))}
          </ul>

          <Pagination
            nextHref={orders.data.nextCursor ? `/orders?cursor=${encodeURIComponent(orders.data.nextCursor)}` : null}
            firstHref="/orders"
            onFirstPage={!cursor}
          />
        </>
      )}
    </div>
  );
}
