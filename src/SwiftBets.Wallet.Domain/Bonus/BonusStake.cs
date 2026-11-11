namespace SwiftBets.Wallet.Domain.Bonus;

/// <summary>How an offer paid for a stake; later winnings are split by it, and a free bet's stake is kept back from them.</summary>
public sealed record BonusStake(
    string StakeKey, Guid AccountId, string Reference, StakeProduct Product, int OddsHundredths, long CashAmount, long BonusAmount,
    Guid? GrantId, Guid? FreeBetId, long FreeBetAmount, long FreeBetRetained)
{
    public long Total => CashAmount + BonusAmount + FreeBetAmount;

    /// <summary>Splits a credit: what the promotions account keeps back (a free bet's stake), the bonus share, and cash.</summary>
    public (long Retain, long Bonus, long Cash) Split(long amount)
    {
        var retain = FreeBetId is null ? 0 : Math.Min(amount, Math.Max(0, FreeBetAmount - FreeBetRetained));
        var rest = amount - retain;
        var bonus = BonusAmount > 0 && CashAmount + BonusAmount > 0 ? rest * BonusAmount / (CashAmount + BonusAmount) : 0;
        return (retain, bonus, rest - bonus);
    }
}
