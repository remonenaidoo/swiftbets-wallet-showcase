INSERT INTO wallet.FreeBets (FreeBetId, AccountId, PromotionId, Amount, MinOddsHundredths, ExpiresAt, State, CreatedAt, UpdatedAt)
VALUES (@FreeBetId, @AccountId, @PromotionId, @Amount, @MinOddsHundredths, @ExpiresAt, @State, @Now, @Now);
