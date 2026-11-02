-- R-05 (EP-74): order funnel per UTC day for orders created in [from, to): created, payment started (at least
-- one payment attempt), paid, and refunded (fully or partly). Days without orders are shown as zero.
-- @from timestamptz, @to timestamptz
WITH days AS (
  SELECT generate_series(
           date_trunc('day', @from AT TIME ZONE 'UTC'),
           date_trunc('day', (@to - interval '1 microsecond') AT TIME ZONE 'UTC'),
           interval '1 day') AS day),
started AS (
  SELECT DISTINCT order_id FROM payments WHERE created_at >= @from)
SELECT to_char(d.day, 'YYYY-MM-DD')                                              AS day,
       COUNT(o.id)                                                               AS created,
       COUNT(s.order_id)                                                         AS payment_started,
       COUNT(o.id) FILTER (WHERE o.paid_at IS NOT NULL)                          AS paid,
       COUNT(o.id) FILTER (WHERE o.status IN ('Refunded', 'PartiallyRefunded'))  AS refunded
FROM days d
LEFT JOIN orders o  ON date_trunc('day', o.created_at AT TIME ZONE 'UTC') = d.day
                   AND o.created_at >= @from AND o.created_at < @to
LEFT JOIN started s ON s.order_id = o.id
GROUP BY d.day
ORDER BY d.day;
