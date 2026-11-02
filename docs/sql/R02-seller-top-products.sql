-- R-02, seller form (EP-72): the seller's top products by units sold in a period, refunded items excluded.
-- Read from the ledger: a Sale entry is created when an item is paid, and a Refund entry when it is refunded.
-- @seller_id uuid, @since timestamptz, @limit int
SELECT oi.product_id, MAX(oi.product_title) AS title,
       COUNT(*) AS units, SUM(l.amount) AS gross,
       ROW_NUMBER() OVER (ORDER BY COUNT(*) DESC, SUM(l.amount) DESC, oi.product_id) AS rank
FROM seller_ledger_entries l
JOIN order_items oi ON oi.id = l.order_item_id
WHERE l.seller_id = @seller_id AND l.entry_type = 'Sale' AND l.created_at >= @since AND oi.status = 'Active'
GROUP BY oi.product_id
ORDER BY rank
LIMIT @limit;
