INSERT INTO wallet.Reservations (ReservationId, AccountId, Amount, Currency, Reference, State, Purpose, BonusAmount, FreeBetId, CreatedAt, UpdatedAt)
VALUES (@ReservationId, @AccountId, @Amount, @Currency, @Reference, @State, @Purpose, @BonusAmount, @FreeBetId, @Now, @Now);
