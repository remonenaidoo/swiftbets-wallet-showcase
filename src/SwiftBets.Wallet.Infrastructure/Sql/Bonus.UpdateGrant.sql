UPDATE wallet.BonusGrants
SET Wagered = @Wagered, State = @State, ClosedAt = CASE WHEN @State = 1 THEN NULL ELSE COALESCE(ClosedAt, @Now) END
WHERE GrantId = @GrantId;
