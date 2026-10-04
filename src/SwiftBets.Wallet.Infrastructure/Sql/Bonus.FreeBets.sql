SELECT FreeBetId, AccountId, PromotionId, Amount, MinOddsHundredths, ExpiresAt, State
FROM wallet.FreeBets
WHERE AccountId = @AccountId AND State = 1 AND ExpiresAt > @Now
ORDER BY ExpiresAt;
