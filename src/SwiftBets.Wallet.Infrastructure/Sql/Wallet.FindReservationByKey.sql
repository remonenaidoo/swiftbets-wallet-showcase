SELECT r.ReservationId, r.AccountId, r.Amount, r.Currency, r.Reference, r.State, r.Purpose, r.BonusAmount, r.FreeBetId
FROM wallet.Postings p
JOIN wallet.Reservations r ON r.ReservationId = p.ReservationId
WHERE p.IdempotencyKey = @IdempotencyKey;
