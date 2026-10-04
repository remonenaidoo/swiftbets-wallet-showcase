-- One UTC day of money movement, split the way the reporting warehouse counts it. Casino postings carry casino: keys, racing postings racing: keys.
SELECT
    COALESCE(SUM(CASE WHEN Kind = 3 AND IdempotencyKey NOT LIKE 'casino:%' AND IdempotencyKey NOT LIKE 'racing:%' THEN Amount END), 0) AS SportsStakes,
    COALESCE(SUM(CASE WHEN Kind = 5 AND IdempotencyKey NOT LIKE 'casino:%' AND IdempotencyKey NOT LIKE 'racing:%' THEN Amount END), 0) AS SportsCredits,
    COALESCE(SUM(CASE WHEN Kind = 6 AND IdempotencyKey NOT LIKE 'casino:%' AND IdempotencyKey NOT LIKE 'racing:%' THEN Amount END), 0) AS SportsDebits,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'casino:%' AND Kind IN (3, 6) THEN Amount END), 0) AS CasinoStaked,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'casino:%' AND Kind IN (4, 5) THEN Amount END), 0) AS CasinoReturned,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'racing:%' AND Kind = 6 THEN Amount END), 0) AS RacingStaked,
    COALESCE(SUM(CASE WHEN IdempotencyKey LIKE 'racing:%' AND Kind = 5 THEN Amount END), 0) AS RacingReturned
FROM wallet.Postings
WHERE PostedAt >= @From AND PostedAt < @To;
