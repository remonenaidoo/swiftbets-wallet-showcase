SELECT TOP (1) StakeKey, AccountId, Reference, Product, OddsHundredths, CashAmount, BonusAmount, GrantId, FreeBetId, FreeBetAmount, FreeBetRetained
FROM wallet.BonusStakes
WHERE AccountId = @AccountId AND Reference = @Reference AND Product = 1
ORDER BY PostedAt DESC;
