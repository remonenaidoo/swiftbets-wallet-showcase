-- Sequence, PostedAt and the balances after are copies for the statement (D196); a missing value is NULL, never a failed write.
INSERT INTO wallet.LedgerEntries (PostingId, AccountId, Bucket, Amount, Sequence, PostedAt, AvailableAfter, BonusAfter)
SELECT @PostingId, @AccountId, @Bucket, @Amount, p.Sequence, p.PostedAt, @AvailableAfter, @BonusAfter
FROM (VALUES (1)) one (x)
LEFT JOIN wallet.Postings p ON p.PostingId = @PostingId;
