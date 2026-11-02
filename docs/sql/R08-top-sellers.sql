-- R-08 (EP-75): top sellers by net sales over a period.
-- @since timestamptz, @limit int
SELECT l.seller_id, sp.display_name,
       SUM(l.amount) FILTER (WHERE l.entry_type IN ('Sale', 'Refund'))            AS net_sales,
       -COALESCE(SUM(l.amount) FILTER (WHERE l.entry_type IN ('PlatformFee', 'FeeRefund')), 0) AS platform_fees,
       COUNT(*) FILTER (WHERE l.entry_type = 'Sale')                              AS units
FROM seller_ledger_entries l
JOIN seller_profiles sp ON sp.id = l.seller_id
WHERE l.created_at >= @since AND l.entry_type <> 'Payout'
GROUP BY l.seller_id, sp.display_name
ORDER BY net_sales DESC, l.seller_id
LIMIT @limit;
