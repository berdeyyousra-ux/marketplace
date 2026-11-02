# R-10: seller monthly revenue from a materialized view (T-054)

The query is `R10-seller-monthly-revenue.sql`, and the view and `report_refreshes` table are created by the `AddSellerMonthlyRevenueView` migration. The Worker job `SellerRevenueViewRefreshJob` runs `REFRESH MATERIALIZED VIEW CONCURRENTLY` every 15 minutes. `CONCURRENTLY` needs the unique index on `(seller_id, month)`. The job then records when the refresh started.

## Why the result stays exact

Ledger entries are only written at the current time: a sale when an item is paid, and a refund when it is refunded. Every month that began before the last refresh started, less a 10-minute margin for transactions still open at that moment, is therefore complete in the view. R-10 reads those months from the view and every later month live from the ledger. Without a recorded refresh, every month is read live, which is exactly R-01. The integration test `R10_AfterRefresh_ReadsCompletedMonthsFromTheViewAndLaterEntriesLive` checks both halves.

## Benchmark

Setup:

- Dataset: `seed-load --orders 1000000 --sellers 500 --buyers 20000 --products 10000 --seed 42`, with 2,644,132 ledger entries.
- Database: PostgreSQL 17 in Docker on an M-series laptop, default settings, warm cache.
- Method: `EXPLAIN ANALYZE` execution time, the median of six runs, with the period ending 2026-12-01.

| Seller (ledger rows) | Months | R-01 | R-10 | Speed-up |
| --- | --- | --- | --- | --- |
| Busiest (42,886) | 12 | 11.6 ms | 0.37 ms | 31x |
| Busiest (42,886) | 24 | 22.1 ms | 0.39 ms | 57x |
| Median (4,491) | 12 | 2.7 ms | 0.22 ms | 12x |
| Median (4,491) | 24 | 3.6 ms | 0.23 ms | 16x |

After the covering ledger index from ADR-0003, R-01 became an index-only scan. For the busiest seller it now takes 5.8 ms over 12 months and 12.1 ms over 24, and for the median seller 0.9 and 1.2 ms. R-10 stays at 0.2 to 0.35 ms, so it still wins by 4 to 35 times and EP-71 keeps using it.

With a cold cache, R-01 took 125 ms for the busiest seller over 24 months. R-01's cost grows with the seller's sales, because it is a bitmap heap scan over `ix_ledger_seller_id_created_at` that touches about 15,000 heap pages for the busiest seller. R-10 reads at most 24 view rows through the unique index, plus the current month's entries.

A full refresh of the view (12,466 rows) took 0.76 s, and readers are not blocked while it runs.

R-01 and R-10 returned identical rows for these runs. EP-71 uses R-10. R-01 remains as `IReports.SellerMonthlyRevenueFromLedgerAsync`, the reference that the tests compare against.
