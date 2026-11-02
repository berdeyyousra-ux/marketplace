-- EP-74 overview totals, shown with R-05 and R-06: net sales and platform fees from the ledger (section 13.1),
-- paid orders, and the share of items paid in [from, to) that were refunded.
-- @from timestamptz, @to timestamptz
SELECT
  (SELECT COALESCE(SUM(amount), 0) FROM seller_ledger_entries
    WHERE entry_type IN ('Sale', 'Refund') AND created_at >= @from AND created_at < @to)           AS net_sales,
  (SELECT -COALESCE(SUM(amount), 0) FROM seller_ledger_entries
    WHERE entry_type IN ('PlatformFee', 'FeeRefund') AND created_at >= @from AND created_at < @to) AS platform_fees,
  (SELECT COUNT(*) FROM orders WHERE paid_at >= @from AND paid_at < @to)                           AS paid_orders,
  (SELECT ROUND(COUNT(*) FILTER (WHERE oi.status = 'Refunded')::numeric / NULLIF(COUNT(*), 0), 4)
     FROM order_items oi JOIN orders o ON o.id = oi.order_id
    WHERE o.paid_at >= @from AND o.paid_at < @to)                                                  AS refund_rate;
