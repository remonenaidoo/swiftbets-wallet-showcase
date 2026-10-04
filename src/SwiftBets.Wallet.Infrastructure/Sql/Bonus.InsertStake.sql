INSERT INTO wallet.BonusStakes (StakeKey, AccountId, Reference, Product, OddsHundredths, CashAmount, BonusAmount, GrantId, FreeBetId, FreeBetAmount, FreeBetRetained, PostedAt)
VALUES (@StakeKey, @AccountId, @Reference, @Product, @OddsHundredths, @CashAmount, @BonusAmount, @GrantId, @FreeBetId, @FreeBetAmount, @FreeBetRetained, @Now);
