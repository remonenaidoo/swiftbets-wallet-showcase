SELECT MAX(LockedUntil) FROM wallet.VoucherAttempts WHERE Subject IN @Subjects AND LockedUntil > @Now;
