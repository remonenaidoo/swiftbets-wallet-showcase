namespace SwiftBets.Wallet.Domain;

/// <summary>The house and funding accounts, one pair per supported currency; ZAR keeps the original ids.</summary>
public static class WellKnownAccounts
{
    /// <summary>The bookmaker: captured stakes land here and winnings are paid from here.</summary>
    public static readonly Guid House = Guid.Parse("00000000-0000-0000-0000-00000000b001");

    /// <summary>The outside world: deposits come from here and withdrawals are paid to it.</summary>
    public static readonly Guid Funding = Guid.Parse("00000000-0000-0000-0000-00000000f001");

    private static readonly Dictionary<string, Guid> PromotionsByCurrency = new(StringComparer.Ordinal)
    {
        ["ZAR"] = Guid.Parse("00000000-0000-0000-0000-00000000c001"),
        ["USD"] = Guid.Parse("00000000-0000-0000-0000-00000000c840"),
    };

    private static readonly Dictionary<string, (Guid House, Guid Funding)> ByCurrency = new(StringComparer.Ordinal)
    {
        ["ZAR"] = (House, Funding),
        ["USD"] = (Guid.Parse("00000000-0000-0000-0000-00000000b840"), Guid.Parse("00000000-0000-0000-0000-00000000f840")),
    };

    public static IReadOnlyCollection<string> Currencies => ByCurrency.Keys;

    public static Guid? HouseFor(string currency) => ByCurrency.TryGetValue(currency, out var pair) ? pair.House : null;

    public static Guid? FundingFor(string currency) => ByCurrency.TryGetValue(currency, out var pair) ? pair.Funding : null;

    /// <summary>The offers budget: bonus grants and free bet stakes come from it; forfeits and kept free bet stakes go back.</summary>
    public static Guid? PromotionsFor(string currency) => PromotionsByCurrency.TryGetValue(currency, out var id) ? id : null;
}
