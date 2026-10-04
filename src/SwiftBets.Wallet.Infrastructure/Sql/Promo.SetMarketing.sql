UPDATE wallet.MarketingPreferences SET OptedOut = @OptedOut, UpdatedAt = @At WHERE UserId = @UserId;
IF @@ROWCOUNT = 0
    INSERT INTO wallet.MarketingPreferences (UserId, OptedOut, UpdatedAt) VALUES (@UserId, @OptedOut, @At);
