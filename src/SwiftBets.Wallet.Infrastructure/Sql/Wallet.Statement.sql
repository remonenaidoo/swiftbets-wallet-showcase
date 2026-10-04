-- Cash (available) and bonus movements of one account per posting, with both balances after each, newest first. Stakes
-- show even when neither balance moved (a free bet).
WITH moves AS (
    SELECT p.Sequence, p.PostingId, p.Kind, p.Reference, p.PostedAt,
           SUM(CASE WHEN e.Bucket = 1 THEN e.Amount ELSE 0 END) AS Amount,
           SUM(CASE WHEN e.Bucket = 3 THEN e.Amount ELSE 0 END) AS BonusAmount
    FROM wallet.LedgerEntries e
    JOIN wallet.Postings p ON p.PostingId = e.PostingId
    WHERE e.AccountId = @AccountId AND (e.Bucket IN (1, 3) OR (e.Bucket = 2 AND p.Kind = 2))
    GROUP BY p.Sequence, p.PostingId, p.Kind, p.Reference, p.PostedAt),
running AS (
    SELECT *, SUM(Amount) OVER (ORDER BY Sequence ROWS UNBOUNDED PRECEDING) AS AvailableAfter,
              SUM(BonusAmount) OVER (ORDER BY Sequence ROWS UNBOUNDED PRECEDING) AS BonusAfter
    FROM moves)
SELECT TOP (@Limit) Sequence, PostingId, Kind, Amount, AvailableAfter, Reference, PostedAt, BonusAmount, BonusAfter
FROM running
WHERE (@Before IS NULL OR Sequence < @Before)
  AND (@From IS NULL OR PostedAt >= @From)
  AND (@To IS NULL OR PostedAt < @To)
ORDER BY Sequence DESC;
