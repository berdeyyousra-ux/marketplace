-- R-04 (EP-76): the products with the highest refund rate for items paid in a period, among products with at
-- least @min_units units sold.
-- @since timestamptz, @min_units int, @limit int
SELECT oi.product_id, MAX(oi.product_title) AS title, oi.seller_id,
       COUNT(*)                                        AS units_sold,
       COUNT(*) FILTER (WHERE oi.status = 'Refunded')  AS units_refunded,
       ROUND(COUNT(*) FILTER (WHERE oi.status = 'Refunded')::numeric / NULLIF(COUNT(*), 0), 4) AS refund_rate
FROM order_items oi
JOIN orders o ON o.id = oi.order_id AND o.paid_at >= @since
GROUP BY oi.product_id, oi.seller_id
HAVING COUNT(*) >= @min_units
ORDER BY refund_rate DESC, units_sold DESC, oi.product_id
LIMIT @limit;
