-- The customer's latest casino stake; a casino credit is split as that stake was paid.
SELECT TOP (1) StakeKey, AccountId, Reference, Product, OddsHundredths, CashAmount, BonusAmount, GrantId, FreeBetId, FreeBetAmount, FreeBetRetained
FROM wallet.BonusStakes
WHERE AccountId = @AccountId AND Product = 2
ORDER BY PostedAt DESC;
