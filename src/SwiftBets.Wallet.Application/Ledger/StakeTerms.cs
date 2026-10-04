using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>What a stake is on and at what lowest price, and whether a free bet pays for it; offers are judged by these.</summary>
public sealed record StakeTerms(StakeProduct Product, int OddsHundredths, Guid? FreeBetId = null)
{
    /// <summary>A stake from a caller that says nothing about it: a sports stake that no bonus money or free bet may pay for.</summary>
    public static StakeTerms Unspecified { get; } = new(StakeProduct.Sport, 0);
}
