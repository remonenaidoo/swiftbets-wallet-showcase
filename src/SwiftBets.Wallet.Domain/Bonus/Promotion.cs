namespace SwiftBets.Wallet.Domain.Bonus;

/// <summary>An offer built in the console. MaxAward is the deposit match cap, or the free bet's value.</summary>
public sealed record Promotion(
    Guid PromotionId, string Name, PromotionKind Kind, string Currency, int MatchPercent, long MaxAward, long MinDeposit,
    int WageringMultiplier, int MinOddsHundredths, byte SportsPercent, byte CasinoPercent, int ValidDays, bool OptInRequired,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Terms, bool IsActive)
{
    public bool IsRunning(DateTimeOffset at) => IsActive && StartsAt <= at && EndsAt > at;

    /// <summary>The bonus a deposit earns: its percentage, capped; zero under the minimum deposit.</summary>
    public long DepositAward(long deposit) =>
        Kind != PromotionKind.DepositMatch || deposit < Math.Max(1, MinDeposit) ? 0 : Math.Min(deposit * MatchPercent / 100, MaxAward);

    /// <summary>Why the offer cannot be saved, or null.</summary>
    public string? Problem() =>
        Name.Trim().Length is < 3 or > 100 ? "Give the offer a name of 3 to 100 characters."
        : !Enum.IsDefined(Kind) ? "Choose what the offer gives."
        : WellKnownAccounts.PromotionsFor(Currency) is null ? "Choose ZAR or USD."
        : Kind == PromotionKind.DepositMatch && MatchPercent is < 1 or > 500 ? "The match must be 1 to 500 percent."
        : MaxAward <= 0 ? (Kind == PromotionKind.DepositMatch ? "Set the most a deposit match can give." : "Set the free bet's value.")
        : MinDeposit < 0 ? "The minimum deposit cannot be negative."
        : WageringMultiplier is < 0 or > 50 ? "Wagering must be 0 to 50 times the bonus."
        : MinOddsHundredths is < 101 or > 100_000 ? "Minimum odds must be 1.01 to 1000."
        : SportsPercent > 100 || CasinoPercent > 100 ? "Contributions must be 0 to 100 percent."
        : ValidDays is < 1 or > 90 ? "Bonuses must last 1 to 90 days."
        : EndsAt <= StartsAt ? "The offer must end after it starts."
        : Terms.Trim().Length is < 1 or > 4000 ? "Add the terms (up to 4000 characters)."
        : null;
}
