SELECT COALESCE((SELECT OptedOut FROM wallet.MarketingPreferences WHERE UserId = @UserId), CAST(0 AS bit));
