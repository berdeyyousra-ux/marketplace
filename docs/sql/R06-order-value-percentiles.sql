-- R-06 (EP-74): order value percentiles for orders paid in [from, to).
-- @from timestamptz, @to timestamptz
SELECT COUNT(*)                                                                AS orders,
       ROUND(percentile_cont(0.50) WITHIN GROUP (ORDER BY total)::numeric, 2)  AS p50,
       ROUND(percentile_cont(0.90) WITHIN GROUP (ORDER BY total)::numeric, 2)  AS p90,
       ROUND(percentile_cont(0.99) WITHIN GROUP (ORDER BY total)::numeric, 2)  AS p99
FROM orders
WHERE paid_at >= @from AND paid_at < @to;
