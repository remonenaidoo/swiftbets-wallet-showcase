INSERT INTO wallet.Vouchers (VoucherId, IssueKey, Last4, Amount, Currency, Source, SourceAccountId, PurchasedBy, BatchId, RecipientEmail, Status, CreatedAt, ExpiresAt, RedeemedBy, RedeemedAt, VoidedBy, VoidReason, VoidedAt, CodeHash)
VALUES (@VoucherId, @IssueKey, @Last4, @Amount, @Currency, @Source, @SourceAccountId, @PurchasedBy, @BatchId, @RecipientEmail, @Status, @CreatedAt, @ExpiresAt,
        @RedeemedBy, @RedeemedAt, @VoidedBy, @VoidReason, @VoidedAt, @CodeHash);
