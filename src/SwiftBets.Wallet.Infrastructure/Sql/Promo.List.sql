SELECT p.PromotionId, p.Name, p.Kind, p.Currency, p.MatchPercent, p.MaxAward, p.MinDeposit, p.WageringMultiplier, p.MinOddsHundredths,
       p.SportsPercent, p.CasinoPercent, p.ValidDays, p.OptInRequired, p.StartsAt, p.EndsAt, p.Terms, p.IsActive, p.CreatedAt, p.UpdatedAt,
       (SELECT COUNT(*) FROM wallet.PromotionAwards w WHERE w.PromotionId = p.PromotionId AND w.Outcome = 1) AS AwardCount
FROM wallet.Promotions p
WHERE @PromotionId IS NULL OR p.PromotionId = @PromotionId
ORDER BY p.StartsAt DESC, p.CreatedAt DESC;
