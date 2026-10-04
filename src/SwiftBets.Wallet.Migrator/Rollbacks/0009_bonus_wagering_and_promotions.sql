-- Rolls back 0009_bonus_wagering_and_promotions. Refuses while any bonus, free bet stake or promotions money exists.
IF EXISTS (SELECT 1 FROM wallet.LedgerEntries e JOIN wallet.Accounts a ON a.AccountId = e.AccountId WHERE a.Kind = 4)
   OR EXISTS (SELECT 1 FROM wallet.Reservations WHERE State = 1 AND (BonusAmount <> 0 OR FreeBetId IS NOT NULL))
    THROW 50001, 'Promotions money has moved; it cannot be rolled back.', 1;
DROP TABLE IF EXISTS wallet.MarketingPreferences;
DROP TABLE IF EXISTS wallet.PromotionAudit;
DROP TABLE IF EXISTS wallet.PromotionAwards;
DROP TABLE IF EXISTS wallet.PromotionOptIns;
DROP TABLE IF EXISTS wallet.Promotions;
DROP TABLE IF EXISTS wallet.BonusStakes;
DROP TABLE IF EXISTS wallet.FreeBets;
DROP TABLE IF EXISTS wallet.BonusGrants;
ALTER TABLE wallet.Reservations DROP CONSTRAINT IF EXISTS DF_Reservations_BonusAmount;
ALTER TABLE wallet.Reservations DROP COLUMN IF EXISTS BonusAmount, FreeBetId;
DELETE FROM wallet.Accounts WHERE Kind = 4;
