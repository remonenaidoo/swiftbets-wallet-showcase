-- Bounded statements (D196): each ledger entry carries its posting's Sequence and PostedAt and the account's cash and bonus
-- balances after that posting, so a statement page reads stored values instead of summing the account's whole history.
-- Nullable and unconstrained: a ledger write never fails because of these columns.
ALTER TABLE wallet.LedgerEntries ADD Sequence bigint NULL, PostedAt datetimeoffset(3) NULL, AvailableAfter bigint NULL, BonusAfter bigint NULL;
GO

UPDATE e SET Sequence = p.Sequence, PostedAt = p.PostedAt
FROM wallet.LedgerEntries e
JOIN wallet.Postings p ON p.PostingId = e.PostingId;

-- The backfill is the old statement's running sums (cash = bucket 1, bonus = bucket 3), per account in posting order.
WITH moves AS (
    SELECT AccountId, Sequence,
           SUM(CASE WHEN Bucket = 1 THEN Amount ELSE 0 END) AS Amount,
           SUM(CASE WHEN Bucket = 3 THEN Amount ELSE 0 END) AS BonusAmount
    FROM wallet.LedgerEntries
    GROUP BY AccountId, Sequence),
running AS (
    SELECT AccountId, Sequence,
           SUM(Amount) OVER (PARTITION BY AccountId ORDER BY Sequence ROWS UNBOUNDED PRECEDING) AS AvailableAfter,
           SUM(BonusAmount) OVER (PARTITION BY AccountId ORDER BY Sequence ROWS UNBOUNDED PRECEDING) AS BonusAfter
    FROM moves)
UPDATE e SET AvailableAfter = r.AvailableAfter, BonusAfter = r.BonusAfter
FROM wallet.LedgerEntries e
JOIN running r ON r.AccountId = e.AccountId AND r.Sequence = e.Sequence;

-- A statement page seeks one account's entries newest first; its date range becomes a Sequence range through the
-- account's own entries by date (never the whole ledger, and correct even if PostedAt and Sequence disagree on order).
CREATE INDEX IX_LedgerEntries_Account_Sequence ON wallet.LedgerEntries (AccountId, Sequence DESC)
    INCLUDE (PostingId, Bucket, Amount, PostedAt, AvailableAfter, BonusAfter);
CREATE INDEX IX_LedgerEntries_Account_PostedAt ON wallet.LedgerEntries (AccountId, PostedAt) INCLUDE (Sequence);
