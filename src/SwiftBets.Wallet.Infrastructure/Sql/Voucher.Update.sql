UPDATE wallet.Vouchers
SET Status = @Status, RedeemedBy = @RedeemedBy, RedeemedAt = @RedeemedAt, VoidedBy = @VoidedBy, VoidReason = @VoidReason, VoidedAt = @VoidedAt
WHERE VoucherId = @VoucherId;
