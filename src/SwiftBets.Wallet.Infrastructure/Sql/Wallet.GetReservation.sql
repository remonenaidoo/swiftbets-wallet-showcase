SELECT ReservationId, AccountId, Amount, Currency, Reference, State, Purpose, BonusAmount, FreeBetId
FROM wallet.Reservations
WHERE ReservationId = @ReservationId;
