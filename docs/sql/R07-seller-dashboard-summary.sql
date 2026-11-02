-- R-07 (EP-70): the seller dashboard summary in one round trip. Money comes from the ledger (section 13.1):
-- balance includes payouts; revenue is net sales (Sale plus Refund). Orders counts distinct orders with a sale.
-- @seller_id uuid, @since timestamptz
SELECT
  (SELECT COALESCE(SUM(amount), 0) FROM seller_ledger_entries
    WHERE seller_id = @seller_id)                                                         AS balance,
  (SELECT COALESCE(SUM(amount), 0) FROM seller_ledger_entries
    WHERE seller_id = @seller_id AND entry_type IN ('Sale', 'Refund') AND created_at >= @since) AS revenue,
  (SELECT COUNT(DISTINCT oi.order_id) FROM seller_ledger_entries l
    JOIN order_items oi ON oi.id = l.order_item_id
    WHERE l.seller_id = @seller_id AND l.entry_type = 'Sale' AND l.created_at >= @since)  AS orders,
  (SELECT COUNT(*) FROM seller_ledger_entries
    WHERE seller_id = @seller_id AND entry_type = 'Refund' AND created_at >= @since)       AS refunds,
  (SELECT COUNT(*) FROM products
    WHERE seller_id = @seller_id AND status = 'Published')                                AS active_products;
