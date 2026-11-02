-- R-03 (EP-77): repeat-buyer cohorts. A buyer's cohort is the month of their first paid order. The shares are of
-- the cohort's buyers who paid for another order within 30, 60 and 90 days of the first.
-- @from timestamptz, @to timestamptz (cohort months whose first payment falls in [from, to))
WITH paid AS (
  SELECT buyer_id, paid_at FROM orders WHERE paid_at IS NOT NULL),
firsts AS (
  SELECT buyer_id, MIN(paid_at) AS first_paid_at FROM paid GROUP BY buyer_id),
cohort AS (
  SELECT f.buyer_id, f.first_paid_at, MIN(p.paid_at) AS next_paid_at
  FROM firsts f
  LEFT JOIN paid p ON p.buyer_id = f.buyer_id AND p.paid_at > f.first_paid_at
  WHERE f.first_paid_at >= @from AND f.first_paid_at < @to
  GROUP BY f.buyer_id, f.first_paid_at)
SELECT to_char(date_trunc('month', first_paid_at AT TIME ZONE 'UTC'), 'YYYY-MM') AS cohort,
       COUNT(*) AS buyers,
       ROUND(COUNT(*) FILTER (WHERE next_paid_at <= first_paid_at + interval '30 days')::numeric / COUNT(*), 4) AS thirty_day_repeat_share,
       ROUND(COUNT(*) FILTER (WHERE next_paid_at <= first_paid_at + interval '60 days')::numeric / COUNT(*), 4) AS sixty_day_repeat_share,
       ROUND(COUNT(*) FILTER (WHERE next_paid_at <= first_paid_at + interval '90 days')::numeric / COUNT(*), 4) AS ninety_day_repeat_share
FROM cohort
GROUP BY 1
ORDER BY 1;
