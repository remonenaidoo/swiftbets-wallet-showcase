-- Bonus money no active grant owns, such as a bonus stake released after its offer ended.
SELECT TOP (@Limit) a.AccountId, a.Currency, a.Bonus
FROM wallet.Accounts a
WHERE a.Kind = 1 AND a.Bonus > 0
  AND NOT EXISTS (SELECT 1 FROM wallet.BonusGrants g WHERE g.AccountId = a.AccountId AND g.State = 1);
