-- Held to the end of the transaction, so a redeem and a void of the same voucher queue behind each other.
SELECT VoucherId, IssueKey, Last4, Amount, Currency, Source, SourceAccountId, PurchasedBy, BatchId, RecipientEmail, Status, CreatedAt, ExpiresAt, RedeemedBy, RedeemedAt, VoidedBy, VoidReason, VoidedAt
FROM wallet.Vouchers WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
WHERE VoucherId = @VoucherId;
