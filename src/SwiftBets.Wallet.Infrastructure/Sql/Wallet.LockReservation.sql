SELECT ReservationId, AccountId, Amount, Currency, Reference, State, Purpose, BonusAmount, FreeBetId
FROM wallet.Reservations WITH (UPDLOCK, ROWLOCK)
WHERE ReservationId = @ReservationId;
