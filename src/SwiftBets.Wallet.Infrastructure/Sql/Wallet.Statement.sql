-- Cash (available) and bonus movements of one account per posting, with both balances after each, newest first (D196).
-- Balances are read from the entries (stored at post time), never summed over history. Stakes show even when neither
-- balance moved (a free bet).
-- The date range becomes this account's Sequence range (IX_LedgerEntries_Account_PostedAt); dates are still checked exactly.
DECLARE @FromSeq bigint = 0, @ToSeq bigint = 9223372036854775807;
IF @From IS NOT NULL OR @To IS NOT NULL
    SELECT @FromSeq = ISNULL(MIN(Sequence), 0), @ToSeq = ISNULL(MAX(Sequence), -1)
    FROM wallet.LedgerEntries
    WHERE AccountId = @AccountId AND (@From IS NULL OR PostedAt >= @From) AND (@To IS NULL OR PostedAt < @To);
-- The page cursor narrows the same range, so the seek has one plain Sequence range (no OR for the optimiser to scan on).
IF @Before IS NOT NULL AND @Before - 1 < @ToSeq
    SET @ToSeq = @Before - 1;

SELECT TOP (@Limit) p.Sequence, p.PostingId, p.Kind, x.Amount, ISNULL(x.AvailableAfter, 0) AS AvailableAfter, p.Reference, p.PostedAt,
       x.BonusAmount, ISNULL(x.BonusAfter, 0) AS BonusAfter
FROM (
    SELECT e.Sequence, e.PostingId,
           SUM(CASE WHEN e.Bucket = 1 THEN e.Amount ELSE 0 END) AS Amount,
           SUM(CASE WHEN e.Bucket = 3 THEN e.Amount ELSE 0 END) AS BonusAmount,
           MAX(e.AvailableAfter) AS AvailableAfter,
           MAX(e.BonusAfter) AS BonusAfter,
           MAX(CASE WHEN e.Bucket IN (1, 3) THEN 1 ELSE 0 END) AS Moved,
           MAX(CASE WHEN e.Bucket = 2 THEN 1 ELSE 0 END) AS Held
    FROM wallet.LedgerEntries e
    WHERE e.AccountId = @AccountId
      AND e.Sequence BETWEEN @FromSeq AND @ToSeq
    GROUP BY e.Sequence, e.PostingId) x
JOIN wallet.Postings p ON p.PostingId = x.PostingId
WHERE (x.Moved = 1 OR (x.Held = 1 AND p.Kind = 2))
  AND (@AnyKind = 1 OR p.Kind IN @Kinds)
  AND (@From IS NULL OR p.PostedAt >= @From)
  AND (@To IS NULL OR p.PostedAt < @To)
ORDER BY p.Sequence DESC
OPTION (RECOMPILE); -- plans for this page's actual range, not the first caller's
