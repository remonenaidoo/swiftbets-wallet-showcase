SELECT TOP (500) AwardId, PromotionId, UserId, TriggerKey, Outcome, Reason, Amount, AwardedAt
FROM wallet.PromotionAwards WHERE PromotionId = @PromotionId ORDER BY AwardedAt DESC;
