SELECT PromotionId, Name, Kind, Currency, MatchPercent, MaxAward, MinDeposit, WageringMultiplier, MinOddsHundredths,
       SportsPercent, CasinoPercent, ValidDays, OptInRequired, StartsAt, EndsAt, Terms, IsActive, CreatedAt, UpdatedAt, 0 AS AwardCount
FROM wallet.Promotions
WHERE Kind = @Kind AND Currency = @Currency AND IsActive = 1 AND StartsAt <= @At AND EndsAt > @At
ORDER BY StartsAt;
