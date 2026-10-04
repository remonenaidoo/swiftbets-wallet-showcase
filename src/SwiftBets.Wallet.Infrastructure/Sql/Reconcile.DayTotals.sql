-- One UTC day of money movement, split the way the reporting warehouse counts it. Casino postings carry casino: keys, racing postings racing: keys.
-- Offer money is read from the promotions accounts' entries: granted out as bonus, staked out as free bets, and returned
-- by forfeits, expiries and kept free bet stakes; conversions are the cash bonus became.
SELECT
    COALESCE(SUM(CASE WHEN Kind = 3 AND IdempotencyKey NOT LIKE 'casino:%' AND IdempotencyKey NOT LIKE 'racing:%' THEN Amount END), 0) AS SportsStakes,
    COALESCE(SUM(CASE WHEN Kind = 5 AND IdempotencyKey NOT LIKE 'casino:%' AND IdempotencyKey NOT LIKE 'racing:%' THEN Amount END), 0) AS SportsCredits,
    COALESCE(SUM(CASE WHEN Kind = 6 AND IdempotencyKey NOT LIKE 'casino:%' AND IdempotencyKey NOT LIKE 'racing:%' THEN Amount END), 0) AS SportsDebits,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'casino:%' AND Kind IN (3, 6) THEN Amount END), 0) AS CasinoStaked,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'casino:%' AND Kind IN (4, 5) THEN Amount END), 0) AS CasinoReturned,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'racing:%' AND Kind = 6 THEN Amount END), 0) AS RacingStaked,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'racing:%' AND Kind = 5 THEN Amount END), 0) AS RacingReturned,
    (SELECT COALESCE(-SUM(e.Amount), 0) FROM wallet.LedgerEntries e JOIN wallet.Postings q ON q.PostingId = e.PostingId JOIN wallet.Accounts a ON a.AccountId = e.AccountId
     WHERE a.Kind = 4 AND q.Kind = 11 AND q.PostedAt >= @From AND q.PostedAt < @To) AS BonusGranted,
    (SELECT COALESCE(-SUM(e.Amount), 0) FROM wallet.LedgerEntries e JOIN wallet.Postings q ON q.PostingId = e.PostingId JOIN wallet.Accounts a ON a.AccountId = e.AccountId
     WHERE a.Kind = 4 AND q.Kind = 2 AND q.PostedAt >= @From AND q.PostedAt < @To) AS FreeBetsStaked,
    (SELECT COALESCE(SUM(e.Amount), 0) FROM wallet.LedgerEntries e JOIN wallet.Postings q ON q.PostingId = e.PostingId JOIN wallet.Accounts a ON a.AccountId = e.AccountId
     WHERE a.Kind = 4 AND e.Amount > 0 AND q.PostedAt >= @From AND q.PostedAt < @To) AS BonusReturned,
    (SELECT COALESCE(SUM(e.Amount), 0) FROM wallet.LedgerEntries e JOIN wallet.Postings q ON q.PostingId = e.PostingId
     WHERE e.Bucket = 1 AND q.Kind = 12 AND q.PostedAt >= @From AND q.PostedAt < @To) AS BonusConverted
FROM wallet.Postings
WHERE PostedAt >= @From AND PostedAt < @To;
