# 0003 - Covering indexes for the report queries

**Status:** Accepted
**Date:** 2026-11-02
**Deciders:** berdeyyousra-ux

## Context

Section 13.4 asks for each report query to be measured on the 1M-order dataset before any index is added. An index is added only when the plan shows it is needed, and each one gets a migration and a short ADR note. The measurements are in `docs/sql/README.md`.

## Decision

The `AddReportingIndexes` migration makes three changes:

- `ix_ledger_seller_id_created_at` is rebuilt as `(seller_id, created_at) INCLUDE (entry_type, amount, order_item_id)`. The seller dashboard's ledger reads (R-07, R-02 seller form, and the live part of R-10) become index-only scans. The busiest seller's summary fell from 17.3 ms to 4.8 ms, because the old index made it fetch about 15,000 heap pages.
- `ix_ledger_created_at (created_at) INCLUDE (seller_id, entry_type, amount)` is added for the platform-wide period sums: the EP-74 overview totals (191 ms to 75 ms) and R-08 top sellers (143 ms to 65 ms). Without it, both scanned the whole ledger.
- `ix_orders_created_at (created_at) INCLUDE (id, status, paid_at)` is added for the R-05 funnel, together with a rewrite of that query. The funnel fell from 123 ms to 64 ms, with no scan of all orders or payments and no sort spilled to disk.

The two ledger indexes include `amount`, the column of a Money complex property. EF Core cannot express that in an index's `INCLUDE`, so the migration creates them in SQL and they are not part of the EF model. `LedgerEntry.CreatedAt` is now mapped explicitly, because the old index had been the only thing keeping it in the model.

Two candidates were rejected. A `(buyer_id, paid_at)` index for R-03 was not used by the planner. An `order_items (order_id) INCLUDE (status)` index for the overview's refund rate was not needed within the budget.

## Consequences

- Every ledger insert (two per paid item) now maintains two wider indexes instead of one narrow one. The ledger is append-only and written in the same transaction as the order, so this adds a few microseconds per sale.
- Index-only scans depend on the visibility map, so autovacuum's insert threshold (PostgreSQL 13 and later) keeps the append-only ledger's map current.
- The migration builds the indexes with a plain `CREATE INDEX`, which blocks ledger writes while it runs. That is acceptable at today's data size. A large production table would need `CREATE INDEX CONCURRENTLY` outside a transaction.
