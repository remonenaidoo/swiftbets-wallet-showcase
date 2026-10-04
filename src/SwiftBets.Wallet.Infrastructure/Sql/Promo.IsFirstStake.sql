-- The customer's earliest captured stake, across their currency accounts.
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM (
        SELECT TOP (1) r.Reference
        FROM wallet.Reservations r
        JOIN wallet.Accounts a ON a.AccountId = r.AccountId
        WHERE a.UserId = @UserId AND r.Purpose = 1 AND r.State = 2
        ORDER BY r.CreatedAt, r.ReservationId) f
    WHERE f.Reference = @Reference) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
