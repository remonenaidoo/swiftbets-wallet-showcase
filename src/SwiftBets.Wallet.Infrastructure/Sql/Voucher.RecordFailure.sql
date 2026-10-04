-- One failed redeem: a new window once the old one has passed, and a lockout when the window reaches the cap.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE wallet.VoucherAttempts WITH (UPDLOCK, HOLDLOCK)
SET Failures = CASE WHEN WindowStart <= @WindowFloor THEN 1 ELSE Failures + 1 END,
    WindowStart = CASE WHEN WindowStart <= @WindowFloor THEN @Now ELSE WindowStart END,
    LockedUntil = CASE WHEN (CASE WHEN WindowStart <= @WindowFloor THEN 1 ELSE Failures + 1 END) >= @MaxFailures THEN @LockUntil ELSE LockedUntil END
WHERE Subject = @Subject;
IF @@ROWCOUNT = 0
    INSERT INTO wallet.VoucherAttempts (Subject, Failures, WindowStart, LockedUntil)
    VALUES (@Subject, 1, @Now, CASE WHEN @MaxFailures <= 1 THEN @LockUntil END);
COMMIT;
