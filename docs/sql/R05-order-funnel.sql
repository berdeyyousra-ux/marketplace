-- R-05 (EP-74): order funnel per UTC day for orders created in [from, to): created, payment started (at least
-- one payment attempt, found through ix_payments_order_id), paid, and refunded (fully or partly). Days without
-- orders are shown as zero.
-- @from timestamptz, @to timestamptz
WITH days AS (
  SELECT generate_series(
           date_trunc('day', @from AT TIME ZONE 'UTC'),
           date_trunc('day', (@to - interval '1 microsecond') AT TIME ZONE 'UTC'),
           interval '1 day') AS day),
per_order AS (
  SELECT date_trunc('day', o.created_at AT TIME ZONE 'UTC') AS day, o.status, o.paid_at,
         EXISTS (SELECT 1 FROM payments p WHERE p.order_id = o.id) AS payment_started
  FROM orders o
  WHERE o.created_at >= @from AND o.created_at < @to),
per_day AS (
  SELECT day,
         COUNT(*)                                                          AS created,
         COUNT(*) FILTER (WHERE payment_started)                           AS payment_started,
         COUNT(*) FILTER (WHERE paid_at IS NOT NULL)                       AS paid,
         COUNT(*) FILTER (WHERE status IN ('Refunded', 'PartiallyRefunded')) AS refunded
  FROM per_order
  GROUP BY day)
SELECT to_char(d.day, 'YYYY-MM-DD')     AS day,
       COALESCE(x.created, 0)          AS created,
       COALESCE(x.payment_started, 0)  AS payment_started,
       COALESCE(x.paid, 0)             AS paid,
       COALESCE(x.refunded, 0)         AS refunded
FROM days d
LEFT JOIN per_day x ON x.day = d.day
ORDER BY d.day;
