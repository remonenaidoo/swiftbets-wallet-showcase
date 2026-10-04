-- Available-bucket movements of one account with the running balance after each, newest first.
WITH moves AS (
    SELECT p.Sequence, p.PostingId, p.Kind, p.Reference, p.PostedAt, e.Amount,
           SUM(e.Amount) OVER (ORDER BY p.Sequence ROWS UNBOUNDED PRECEDING) AS AvailableAfter
    FROM wallet.LedgerEntries e
    JOIN wallet.Postings p ON p.PostingId = e.PostingId
    WHERE e.AccountId = @AccountId AND e.Bucket = 1)
SELECT TOP (@Limit) Sequence, PostingId, Kind, Amount, AvailableAfter, Reference, PostedAt
FROM moves
WHERE (@Before IS NULL OR Sequence < @Before)
  AND (@From IS NULL OR PostedAt >= @From)
  AND (@To IS NULL OR PostedAt < @To)
ORDER BY Sequence DESC;
