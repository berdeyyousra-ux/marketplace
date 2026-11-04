import Link from "next/link";
import { currentUser, serverGet } from "@/lib/api";
import { date } from "@/lib/format";
import type { ItemList, MonthlyRevenue, Page, RecentOrderLine, SellerProductSummary, SellerSummary, TopProduct } from "@/lib/types";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatTile } from "@/components/ui/StatTile";
import { Money } from "@/components/ui/Money";
import { EmptyState } from "@/components/ui/EmptyState";
import { StatusBadge } from "@/components/ui/Badge";
import { FormAlert } from "@/components/FormAlert";
import { RevenueChart } from "@/components/RevenueChart";
import { AuthShell } from "@/components/AuthShell";
import { BecomeSellerForm } from "./BecomeSellerForm";
import { ListingRow } from "./ListingRow";

const primaryLink = "inline-flex items-center rounded-control border border-cta bg-cta px-4 py-2 type-label text-cta-contrast hover:border-cta-hover hover:bg-cta-hover";
const th = "px-4 py-3 type-micro text-muted";

function percent(part: number, whole: number) {
  if (whole === 0) return "–";
  return new Intl.NumberFormat("en-IE", { style: "percent", maximumFractionDigits: 1 }).format(part / whole);
}

// EP-70 to EP-73 (R-07, R-01, top products, recent sales), the seller's listings, and EP-27 to become a seller.
export default async function SellerPage() {
  const me = await currentUser();
  if (!me) {
    await serverGet("/me", "/seller");
    return null;
  }

  if (!me.roles.includes("Seller")) {
    return (
      <AuthShell title="Sell on Marketplace" description="Open a shop to list digital products and follow your sales. It takes a minute.">
        <BecomeSellerForm defaultName={me.displayName} />
      </AuthShell>
    );
  }

  const [summary, months, top, recent, listings] = await Promise.all([
    serverGet<SellerSummary>("/sellers/me/dashboard/summary", "/seller"),
    serverGet<ItemList<MonthlyRevenue>>("/sellers/me/dashboard/revenue-by-month?months=12", "/seller"),
    serverGet<ItemList<TopProduct>>("/sellers/me/dashboard/top-products?days=90&limit=5", "/seller"),
    serverGet<Page<RecentOrderLine>>("/sellers/me/dashboard/recent-orders?limit=8", "/seller"),
    serverGet<Page<SellerProductSummary>>("/sellers/me/products?limit=100", "/seller"),
  ]);
  const hasSales = months.ok && months.data.items.some((month) => Number(month.netSales.amount) !== 0);

  return (
    <div className="container-page flex flex-col gap-10 py-8">
      <PageHeader
        title={me.sellerProfile?.displayName ?? "Your shop"}
        description={summary.ok ? undefined : "Figures could not be loaded."}
        action={<Link href="/seller/products/new" className={primaryLink}>New listing</Link>}
      />

      <section aria-label="Last 30 days" className="-mt-6 flex flex-col gap-4">
        {summary.ok ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile label="Net sales, 30 days" value={<Money value={summary.data.revenueLast30Days} />} detail={<>Balance <Money value={summary.data.balance} /></>} />
            <StatTile label="Orders, 30 days" value={summary.data.ordersLast30Days.toLocaleString("en-IE")} />
            <StatTile
              label="Refund rate, 30 days"
              value={percent(summary.data.refundsLast30Days, summary.data.ordersLast30Days)}
              detail={`${summary.data.refundsLast30Days} ${summary.data.refundsLast30Days === 1 ? "refund" : "refunds"} on ${summary.data.ordersLast30Days} orders`}
            />
            <StatTile label="Active listings" value={summary.data.activeProducts.toLocaleString("en-IE")} detail="Published and for sale" />
          </div>
        ) : (
          <FormAlert>The summary could not be loaded. Refresh the page to try again.</FormAlert>
        )}

        {!months.ok ? (
          <FormAlert>Monthly revenue could not be loaded.</FormAlert>
        ) : hasSales ? (
          <RevenueChart months={months.data.items} />
        ) : (
          <EmptyState message="No sales yet. Your monthly revenue appears here after the first order." />
        )}
      </section>

      <div className="grid grid-cols-1 gap-10 lg:grid-cols-2">
        <section className="flex flex-col gap-4">
          <h2 className="type-section m-0 text-text">Top products, 90 days</h2>
          {top.ok && top.data.items.length > 0 ? (
            <div className="overflow-x-auto rounded-card border border-border bg-surface">
              <table className="w-full border-collapse">
                <thead>
                  <tr className="border-b border-border text-left">
                    <th scope="col" className={th}>Product</th>
                    <th scope="col" className={`${th} text-right`}>Units</th>
                    <th scope="col" className={`${th} text-right`}>Revenue</th>
                  </tr>
                </thead>
                <tbody>
                  {top.data.items.map((product) => (
                    <tr key={product.productId} className="border-b border-border last:border-b-0">
                      <td className="px-4 py-3 type-label text-text"><Link href={`/products/${product.productId}`} className="text-text hover:text-accent-strong">{product.title}</Link></td>
                      <td className="px-4 py-3 text-right type-label tabular-nums text-text">{product.units}</td>
                      <td className="px-4 py-3 text-right type-label text-text"><Money value={product.gross} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="m-0 type-body text-muted">No sales in the last 90 days.</p>
          )}
        </section>

        <section className="flex flex-col gap-4">
          <h2 className="type-section m-0 text-text">Recent sales</h2>
          {recent.ok && recent.data.items.length > 0 ? (
            <div className="overflow-x-auto rounded-card border border-border bg-surface">
              <table className="w-full border-collapse">
                <thead>
                  <tr className="border-b border-border text-left">
                    <th scope="col" className={th}>Product</th>
                    <th scope="col" className={th}>Paid</th>
                    <th scope="col" className={`${th} text-right`}>Your share</th>
                  </tr>
                </thead>
                <tbody>
                  {recent.data.items.map((line) => (
                    <tr key={line.orderItemId} className="border-b border-border last:border-b-0">
                      <td className="px-4 py-3 type-label text-text">
                        <span className="flex flex-wrap items-center gap-2">{line.productTitle}{line.status !== "Active" && <StatusBadge status={line.status} />}</span>
                      </td>
                      <td className="px-4 py-3 type-label whitespace-nowrap text-muted">{date(line.paidAt)}</td>
                      <td className="px-4 py-3 text-right type-label text-text"><Money value={line.sellerAmount} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="m-0 type-body text-muted">No sales yet.</p>
          )}
        </section>
      </div>

      <section className="flex flex-col gap-4" aria-labelledby="listings">
        <h2 id="listings" className="type-section m-0 text-text">Listings</h2>
        {!listings.ok ? (
          <FormAlert>Your listings could not be loaded.</FormAlert>
        ) : listings.data.items.length === 0 ? (
          <EmptyState message="You have no listings yet." action={<Link href="/seller/products/new" className={primaryLink}>Create your first listing</Link>} />
        ) : (
          <ul className="m-0 flex list-none flex-col rounded-card border border-border bg-surface p-0">
            {listings.data.items.map((product) => <ListingRow key={product.id} product={product} />)}
          </ul>
        )}
      </section>
    </div>
  );
}
