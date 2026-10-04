-- Vouchers the customer bought or redeemed, newest first.
SELECT TOP (@Limit) VoucherId, IssueKey, Last4, Amount, Currency, Source, SourceAccountId, PurchasedBy, BatchId, RecipientEmail, Status, CreatedAt, ExpiresAt, RedeemedBy, RedeemedAt, VoidedBy, VoidReason, VoidedAt
FROM wallet.Vouchers
WHERE PurchasedBy = @UserId OR RedeemedBy = @UserId
ORDER BY CreatedAt DESC;
