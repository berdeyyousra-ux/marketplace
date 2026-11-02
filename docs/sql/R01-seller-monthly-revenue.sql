-- R-01 (EP-71): monthly net sales, platform fees, seller net and running seller net for one seller.
-- Months without entries are shown as zero (generate_series left join). Payouts are excluded (section 13.1).
-- @seller_id uuid, @from timestamptz (first month start, inclusive), @to timestamptz (exclusive)
WITH months AS (
  SELECT generate_series(
           date_trunc('month', @from AT TIME ZONE 'UTC'),
           date_trunc('month', (@to - interval '1 microsecond') AT TIME ZONE 'UTC'),
           interval '1 month') AS month),
monthly AS (
  SELECT date_trunc('month', created_at AT TIME ZONE 'UTC') AS month,
         SUM(amount) FILTER (WHERE entry_type IN ('Sale', 'Refund'))           AS net_sales,
         SUM(amount) FILTER (WHERE entry_type IN ('PlatformFee', 'FeeRefund')) AS fees
  FROM seller_ledger_entries
  WHERE seller_id = @seller_id AND created_at >= @from AND created_at < @to AND entry_type <> 'Payout'
  GROUP BY 1)
SELECT to_char(m.month, 'YYYY-MM')                                              AS month,
       COALESCE(x.net_sales, 0)                                                 AS net_sales,
       -COALESCE(x.fees, 0)                                                     AS platform_fees,
       COALESCE(x.net_sales, 0) + COALESCE(x.fees, 0)                           AS seller_net,
       SUM(COALESCE(x.net_sales, 0) + COALESCE(x.fees, 0)) OVER (ORDER BY m.month) AS running_seller_net
FROM months m
LEFT JOIN monthly x ON x.month = m.month
ORDER BY m.month;
