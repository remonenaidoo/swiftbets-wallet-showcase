SELECT PromotionId, Actor, Action, Detail, At FROM wallet.PromotionAudit WHERE PromotionId = @PromotionId ORDER BY AuditId DESC;
