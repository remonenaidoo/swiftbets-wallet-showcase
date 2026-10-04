SELECT TOP (@Limit) GrantId, AccountId, PromotionId, Amount, WageringTarget, Wagered, MinOddsHundredths, SportsPercent, CasinoPercent, ExpiresAt, State
FROM wallet.BonusGrants
WHERE State = 1 AND (ExpiresAt <= @Now OR Wagered >= WageringTarget)
ORDER BY ExpiresAt;
