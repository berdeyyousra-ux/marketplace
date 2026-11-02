-- R-10 (EP-71): R-01 served from mv_seller_monthly_revenue. Ledger entries are only ever written at the current
-- time, so months that began before the view's last refresh (less a 10-minute margin for transactions still
-- open when it started) are complete in the view; later months are read live from the ledger. Without a recorded
-- refresh every month is read live, which is exactly R-01.
-- @seller_id uuid, @from timestamptz (first month start, inclusive), @to timestamptz (exclusive)
WITH bounds AS (
  SELECT date_trunc('month', @from AT TIME ZONE 'UTC') AS first_month,
         GREATEST(
           date_trunc('month', @from AT TIME ZONE 'UTC'),
           COALESCE((SELECT date_trunc('month', (refreshed_at - interval '10 minutes') AT TIME ZONE 'UTC')
                     FROM report_refreshes WHERE view_name = 'mv_seller_monthly_revenue'),
                    '-infinity'::timestamp)) AS live_month),
months AS (
  SELECT generate_series(
           date_trunc('month', @from AT TIME ZONE 'UTC'),
           date_trunc('month', (@to - interval '1 microsecond') AT TIME ZONE 'UTC'),
           interval '1 month') AS month),
stored AS (
  SELECT v.month, v.net_sales, v.fees
  FROM mv_seller_monthly_revenue v, bounds b
  WHERE v.seller_id = @seller_id AND v.month >= b.first_month AND v.month < b.live_month),
live AS (
  SELECT date_trunc('month', l.created_at AT TIME ZONE 'UTC')                          AS month,
         SUM(l.amount) FILTER (WHERE l.entry_type IN ('Sale', 'Refund'))           AS net_sales,
         SUM(l.amount) FILTER (WHERE l.entry_type IN ('PlatformFee', 'FeeRefund')) AS fees
  FROM seller_ledger_entries l, bounds b
  WHERE l.seller_id = @seller_id AND l.created_at >= (b.live_month AT TIME ZONE 'UTC') AND l.created_at >= @from
    AND l.created_at < @to AND l.entry_type <> 'Payout'
  GROUP BY 1),
monthly AS (
  SELECT month, net_sales, fees FROM stored
  UNION ALL
  SELECT month, net_sales, fees FROM live)
SELECT to_char(m.month, 'YYYY-MM')                                                 AS month,
       COALESCE(x.net_sales, 0)                                                    AS net_sales,
       -COALESCE(x.fees, 0)                                                        AS platform_fees,
       COALESCE(x.net_sales, 0) + COALESCE(x.fees, 0)                              AS seller_net,
       SUM(COALESCE(x.net_sales, 0) + COALESCE(x.fees, 0)) OVER (ORDER BY m.month) AS running_seller_net
FROM months m
LEFT JOIN monthly x ON x.month = m.month
ORDER BY m.month;
