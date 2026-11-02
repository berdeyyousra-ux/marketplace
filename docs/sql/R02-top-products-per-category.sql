-- R-02 (admin): top products per category by units sold in a period, refunded items excluded.
-- @since timestamptz, @per_category int
SELECT category_id, product_id, title, units, gross, rank
FROM (
  SELECT p.category_id, p.id AS product_id, p.title,
         COUNT(*) AS units, SUM(oi.unit_price) AS gross,
         ROW_NUMBER() OVER (PARTITION BY p.category_id
                            ORDER BY COUNT(*) DESC, SUM(oi.unit_price) DESC, p.id) AS rank
  FROM order_items oi
  JOIN orders o   ON o.id = oi.order_id AND o.paid_at >= @since
  JOIN products p ON p.id = oi.product_id
  WHERE oi.status = 'Active'
  GROUP BY p.category_id, p.id, p.title) ranked
WHERE rank <= @per_category
ORDER BY category_id, rank;
