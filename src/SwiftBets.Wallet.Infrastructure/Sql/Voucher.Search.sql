SELECT TOP (@Limit) VoucherId, IssueKey, Last4, Amount, Currency, Source, SourceAccountId, PurchasedBy, BatchId, RecipientEmail, Status, CreatedAt, ExpiresAt, RedeemedBy, RedeemedAt, VoidedBy, VoidReason, VoidedAt
FROM wallet.Vouchers
WHERE (@CodeHash IS NULL OR CodeHash = @CodeHash)
  AND (@Last4 IS NULL OR Last4 = @Last4)
  AND (@UserId IS NULL OR PurchasedBy = @UserId OR RedeemedBy = @UserId)
  AND (@BatchId IS NULL OR BatchId = @BatchId)
  AND (@Status IS NULL OR Status = @Status)
ORDER BY CreatedAt DESC
OPTION (RECOMPILE);
