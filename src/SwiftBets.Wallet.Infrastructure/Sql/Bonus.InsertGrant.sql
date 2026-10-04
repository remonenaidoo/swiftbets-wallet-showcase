INSERT INTO wallet.BonusGrants (GrantId, AccountId, PromotionId, Amount, WageringTarget, Wagered, MinOddsHundredths, SportsPercent, CasinoPercent, ExpiresAt, State, CreatedAt)
VALUES (@GrantId, @AccountId, @PromotionId, @Amount, @WageringTarget, @Wagered, @MinOddsHundredths, @SportsPercent, @CasinoPercent, @ExpiresAt, @State, @Now);
