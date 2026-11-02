import { currentUser, serverGet } from "@/lib/api";
import { date, money } from "@/lib/format";
import type { ItemList, MonthlyRevenue, Page, RecentOrderLine, SellerSummary, TopProduct } from "@/lib/types";
import { StatusBadge } from "@/components/StatusBadge";
import { BecomeSellerForm } from "./BecomeSellerForm";

// EP-70 to EP-73 and US-16; EP-27 to become a seller.
export default async function SellerPage() {
  const me = await currentUser();
  if (!me) {
    await serverGet("/me", "/seller");
    return null;
  }

  if (!me.roles.includes("Seller")) {
    return (
      <div className="stack">
        <h1>Sell on Marketplace</h1>
        <p>Create a seller profile to list digital products and see your sales.</p>
        <BecomeSellerForm defaultName={me.displayName} />
      </div>
    );
  }

  const [summary, months, top, recent] = await Promise.all([
    serverGet<SellerSummary>("/sellers/me/dashboard/summary", "/seller"),
    serverGet<ItemList<MonthlyRevenue>>("/sellers/me/dashboard/revenue-by-month?months=12", "/seller"),
    serverGet<ItemList<TopProduct>>("/sellers/me/dashboard/top-products?days=90&limit=5", "/seller"),
    serverGet<Page<RecentOrderLine>>("/sellers/me/dashboard/recent-orders?limit=10", "/seller"),
  ]);
  const maxNet = Math.max(1, ...(months.ok ? months.data.items.map((month) => Math.abs(Number(month.netSales.amount))) : [1]));

  return (
    <div className="stack">
      <h1>{me.sellerProfile?.displayName ?? "Seller"} dashboard</h1>
      {summary.ok && (
        <div className="stats">
          <div className="card"><div className="small muted">Balance</div><div className="stat-value">{money(summary.data.balance)}</div></div>
          <div className="card"><div className="small muted">Revenue, 30 days</div><div className="stat-value">{money(summary.data.revenueLast30Days)}</div></div>
          <div className="card"><div className="small muted">Orders, 30 days</div><div className="stat-value">{summary.data.ordersLast30Days}</div></div>
          <div className="card"><div className="small muted">Refunds, 30 days</div><div className="stat-value">{summary.data.refundsLast30Days}</div></div>
          <div className="card"><div className="small muted">Active products</div><div className="stat-value">{summary.data.activeProducts}</div></div>
        </div>
      )}

      <h2>Revenue by month</h2>
      {months.ok && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Month</th><th className="num">Net sales</th><th className="num">Fees</th><th className="num">Seller net</th><th className="num">Running total</th><th style={{ width: "25%" }} /></tr></thead>
            <tbody>
              {months.data.items.map((month) => (
                <tr key={month.month}>
                  <td>{month.month}</td>
                  <td className="num">{money(month.netSales)}</td>
                  <td className="num">{money(month.platformFees)}</td>
                  <td className="num">{money(month.sellerNet)}</td>
                  <td className="num">{money(month.runningSellerNet)}</td>
                  <td><div className="bar" style={{ width: `${(Math.abs(Number(month.netSales.amount)) / maxNet) * 100}%` }} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <h2>Top products, 90 days</h2>
      {top.ok && top.data.items.length > 0 ? (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Product</th><th className="num">Units</th><th className="num">Gross</th></tr></thead>
            <tbody>
              {top.data.items.map((product) => (
                <tr key={product.productId}><td>{product.title}</td><td className="num">{product.units}</td><td className="num">{money(product.gross)}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : <p className="muted">No sales in the last 90 days.</p>}

      <h2>Recent orders</h2>
      {recent.ok && recent.data.items.length > 0 ? (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Paid</th><th>Product</th><th>Status</th><th className="num">Price</th><th className="num">Your share</th></tr></thead>
            <tbody>
              {recent.data.items.map((line) => (
                <tr key={line.orderItemId}>
                  <td>{date(line.paidAt)}</td>
                  <td>{line.productTitle}</td>
                  <td><StatusBadge value={line.status} /></td>
                  <td className="num">{money(line.unitPrice)}</td>
                  <td className="num">{money(line.sellerAmount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : <p className="muted">No orders yet.</p>}
    </div>
  );
}
