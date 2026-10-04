SELECT FreeBetId, AccountId, PromotionId, Amount, MinOddsHundredths, ExpiresAt, State
FROM wallet.FreeBets WITH (UPDLOCK, ROWLOCK)
WHERE FreeBetId = @FreeBetId;
