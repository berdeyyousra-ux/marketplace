# Report queries and their plans (T-055)

Each report from section 13.2 is one file here, `RXX-name.sql`. The files are embedded into Infrastructure at build time, so this is the only copy. `plans/` holds the `EXPLAIN (ANALYZE, BUFFERS)` output from before and after tuning. The view-backed R-10 has its own note, [R10-seller-monthly-revenue.md](R10-seller-monthly-revenue.md).

## Setup

- **Dataset:** `seed-load --orders 1000000 --sellers 500 --buyers 20000 --products 10000 --seed 42`. That gives 1,500,246 order items, 952,323 payments and 2,644,132 ledger entries (see [seeding](../seeding.md)).
- **Database:** PostgreSQL 17 in Docker on an M-series laptop, with default settings (`work_mem` 4 MB). `VACUUM ANALYZE` ran before each round.
- **Parameters:** the busiest seller (42,886 ledger entries), a 30-day period ending 2026-11-03, and twelve cohort months for R-03.
- **Times:** the median of five warm `EXPLAIN ANALYZE` runs.

## Results

| Query | Used by | Before | After | Change |
| --- | --- | --- | --- | --- |
| R-01 seller monthly revenue (24 months) | reference for EP-71 | 22.1 ms | 12.1 ms | covering ledger index |
| R-10 the same from the view | EP-71 | — | 0.35 ms | materialized view (T-054) |
| R-02 seller top products | EP-72 | 2.6 ms | 1.1 ms | covering ledger index |
| R-02 top products per category | admin | 91 ms | 92 ms | none |
| R-03 repeat-buyer cohorts | EP-77 | 261 ms | 265 ms | none (see below) |
| R-04 refund rates by seller | EP-76 | 66 ms | 64 ms | none |
| R-04 refund rates by product | EP-76 | 69 ms | 69 ms | none |
| R-05 order funnel | EP-74 | 123 ms | 64 ms | rewrite and `ix_orders_created_at` |
| Overview totals | EP-74 | 191 ms | 75 ms | `ix_ledger_created_at` |
| R-06 order value percentiles | EP-74 | 15.6 ms | 14.9 ms | none |
| R-07 seller dashboard summary | EP-70 | 17.3 ms | 4.8 ms | covering ledger index |
| R-08 top sellers | EP-75 | 143 ms | 65 ms | covering ledger index |
| R-09 seller recent orders | EP-73 | 0.12 ms | 0.12 ms | none needed |
| Product search (section 13.3) | EP-21 | 0.5 ms | 0.5 ms | none needed |

The section 13.4 targets are a dashboard summary p95 under 150 ms, search under 100 ms and recent orders under 50 ms. The worst seller's summary takes 4.8 ms, search 0.5 ms and recent orders 0.12 ms, so all three hold with a wide margin. The indexes are recorded in [ADR-0003](../adr/0003-reporting-indexes.md) and created by the `AddReportingIndexes` migration.

## Analysis

**Ledger reads for one seller (R-07, R-01, R-02 seller).**
- *Before:* the original `ix_ledger_seller_id_created_at` found the busiest seller's 42,886 entries quickly, but then made a bitmap heap scan that read about 15,000 heap pages to get `entry_type` and `amount`. That fetch was most of R-07's 17 ms ([before](plans/R07-seller-dashboard-summary.before.txt)).
- *After:* the index now carries `entry_type`, `amount` and `order_item_id` in `INCLUDE`. Every ledger subquery in R-07 became an index-only scan with no heap fetches, and R-07 dropped to 4.8 ms ([after](plans/R07-seller-dashboard-summary.after.txt)).
- The ledger is append-only, so its visibility map stays set and index-only scans stay index-only.

**Ledger reads across all sellers (R-08 and the overview totals).**
- *Before:* both filter the whole ledger by `created_at`, which no index led with, so they made a parallel sequential scan over 37,000 pages. For R-08 the result also spilled a sort to disk ([before](plans/R08-top-sellers.before.txt)).
- *After:* with `ix_ledger_created_at (created_at) INCLUDE (seller_id, entry_type, amount)`, the overview's two ledger sums became index-only range scans ([after](plans/R05-overview-totals.after.txt)). For R-08, the planner chose a nested loop over the 500 sellers through the per-seller covering index instead, which also halved the time ([after](plans/R08-top-sellers.after.txt)).
- *Remaining cost:* the overview's refund-rate subquery now dominates. It sequentially scans `order_items` to join the period's 70,000 paid orders. An `order_items (order_id) INCLUDE (status)` index would remove that scan. It was not added, because the admin overview is well inside its budget and every index slows checkout's inserts.

**The order funnel (R-05).**
- *Before:* the first version scanned all of `orders` and all of `payments`, hash-joined the payments, and then sorted 81,763 rows by day. That sort spilled to disk at the default `work_mem` ([before](plans/R05-order-funnel.before.txt)).
- *After:* the rewrite reads only the period's orders through `ix_orders_created_at (created_at) INCLUDE (id, status, paid_at)`, an index-only scan. It checks each order for a payment with an `EXISTS` that is an index-only probe of `ix_payments_order_id`, and aggregates to 31 day rows before joining the calendar, so nothing large is sorted ([after](plans/R05-order-funnel.after.txt)).
- *Remaining cost:* the 81,763 `EXISTS` probes are now most of the 64 ms.

**Queries left alone.**
- *R-03:* has to find each buyer's first paid order across the whole history, so it reads every paid order whatever the period ([plan](plans/R03-repeat-buyer-cohorts.before.txt)). A `(buyer_id, paid_at)` partial index was tried and the planner did not use it; the hash aggregate over 860,000 rows is cheaper. If the cohort report becomes hot, the next step is a materialized first-purchase table, refreshed like R-10.
- *R-02 per category and R-04:* join the period's paid orders to `order_items` by hash after a sequential scan of the items ([plan](plans/R02-top-products-per-category.before.txt)). They are admin reports under 100 ms and were left alone.
- *R-09 and search:* already use their indexes, so nothing was changed.
