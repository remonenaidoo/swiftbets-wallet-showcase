SELECT GrantId, AccountId, PromotionId, Amount, WageringTarget, Wagered, MinOddsHundredths, SportsPercent, CasinoPercent, ExpiresAt, State
FROM wallet.BonusGrants
WHERE AccountId = @AccountId AND State = 1;
