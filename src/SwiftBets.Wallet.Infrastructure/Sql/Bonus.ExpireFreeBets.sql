UPDATE wallet.FreeBets SET State = 3, UpdatedAt = @Now WHERE State = 1 AND ExpiresAt <= @Now;
