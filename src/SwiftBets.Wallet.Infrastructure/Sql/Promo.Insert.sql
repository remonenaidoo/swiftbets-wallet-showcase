INSERT INTO wallet.Promotions (PromotionId, Name, Kind, Currency, MatchPercent, MaxAward, MinDeposit, WageringMultiplier, MinOddsHundredths,
                               SportsPercent, CasinoPercent, ValidDays, OptInRequired, StartsAt, EndsAt, Terms, IsActive, CreatedAt, UpdatedAt)
VALUES (@PromotionId, @Name, @Kind, @Currency, @MatchPercent, @MaxAward, @MinDeposit, @WageringMultiplier, @MinOddsHundredths,
        @SportsPercent, @CasinoPercent, @ValidDays, @OptInRequired, @StartsAt, @EndsAt, @Terms, @IsActive, @Now, @Now);
