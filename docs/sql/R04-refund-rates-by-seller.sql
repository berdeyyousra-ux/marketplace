-- R-04 (EP-76): refund rate per seller for items paid in a period.
-- @since timestamptz
SELECT oi.seller_id, sp.display_name,
       COUNT(*)                                        AS units_sold,
       COUNT(*) FILTER (WHERE oi.status = 'Refunded')  AS units_refunded,
       ROUND(COUNT(*) FILTER (WHERE oi.status = 'Refunded')::numeric / NULLIF(COUNT(*), 0), 4) AS refund_rate
FROM order_items oi
JOIN orders o           ON o.id = oi.order_id AND o.paid_at >= @since
JOIN seller_profiles sp ON sp.id = oi.seller_id
GROUP BY oi.seller_id, sp.display_name
ORDER BY refund_rate DESC, units_sold DESC, oi.seller_id;
