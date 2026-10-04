INSERT INTO wallet.PromotionOptIns (PromotionId, UserId, OptedInAt)
SELECT @PromotionId, @UserId, @At
WHERE NOT EXISTS (SELECT 1 FROM wallet.PromotionOptIns WHERE PromotionId = @PromotionId AND UserId = @UserId);
