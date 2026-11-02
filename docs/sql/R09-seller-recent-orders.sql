-- R-09 (EP-73): the seller's recent sales, newest first, as a keyset page on (paid_at, id) of the Sale ledger
-- entry, which is created when the item is paid. The first page passes 9999-12-31 and the maximum uuid.
-- @seller_id uuid, @last_paid_at timestamptz, @last_id uuid, @limit int
SELECT l.id AS entry_id, l.created_at AS paid_at, oi.order_id, oi.id AS order_item_id, oi.product_id,
       oi.product_title, oi.unit_price, oi.seller_amount, oi.status
FROM seller_ledger_entries l
JOIN order_items oi ON oi.id = l.order_item_id
WHERE l.seller_id = @seller_id AND l.entry_type = 'Sale'
  AND (l.created_at, l.id) < (@last_paid_at, @last_id)
ORDER BY l.created_at DESC, l.id DESC
LIMIT @limit;
