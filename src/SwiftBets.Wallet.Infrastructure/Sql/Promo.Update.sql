UPDATE wallet.Promotions
SET Name = @Name, Kind = @Kind, Currency = @Currency, MatchPercent = @MatchPercent, MaxAward = @MaxAward, MinDeposit = @MinDeposit,
    WageringMultiplier = @WageringMultiplier, MinOddsHundredths = @MinOddsHundredths, SportsPercent = @SportsPercent, CasinoPercent = @CasinoPercent,
    ValidDays = @ValidDays, OptInRequired = @OptInRequired, StartsAt = @StartsAt, EndsAt = @EndsAt, Terms = @Terms, IsActive = @IsActive, UpdatedAt = @Now
WHERE PromotionId = @PromotionId;
