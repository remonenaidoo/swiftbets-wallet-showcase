namespace SwiftBets.Wallet.Domain.Bonus;

/// <summary>
/// Bonus money given by an offer and what must be staked before it becomes cash: the target is the grant times the
/// offer's multiplier, and each stake counts at its product's percentage, sports stakes only at or above the minimum odds.
/// One grant per account is active at a time; the account's bonus balance belongs to it.
/// </summary>
public sealed class BonusGrant(
    Guid grantId, Guid accountId, Guid? promotionId, long amount, long wageringTarget, long wagered, int minOddsHundredths,
    byte sportsPercent, byte casinoPercent, DateTimeOffset expiresAt, BonusState state)
{
    public Guid GrantId { get; } = grantId;

    public Guid AccountId { get; } = accountId;

    public Guid? PromotionId { get; } = promotionId;

    public long Amount { get; } = amount;

    public long WageringTarget { get; } = wageringTarget;

    public long Wagered { get; private set; } = wagered;

    public int MinOddsHundredths { get; } = minOddsHundredths;

    public byte SportsPercent { get; } = sportsPercent;

    public byte CasinoPercent { get; } = casinoPercent;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public BonusState State { get; private set; } = state;

    public bool IsMet => Wagered >= WageringTarget;

    public bool IsLive(DateTimeOffset now) => State == BonusState.Active && ExpiresAt > now;

    /// <summary>Whether bonus money may pay for this stake, and whether the stake counts towards wagering.</summary>
    public bool Allows(StakeProduct product, int oddsHundredths) =>
        product == StakeProduct.Sport ? SportsPercent > 0 && oddsHundredths >= MinOddsHundredths : CasinoPercent > 0;

    /// <summary>Counts a stake towards wagering and returns how much it added.</summary>
    public long Contribute(long stake, StakeProduct product, int oddsHundredths)
    {
        if (State != BonusState.Active || stake <= 0 || !Allows(product, oddsHundredths))
        {
            return 0;
        }

        var added = stake * (product == StakeProduct.Sport ? SportsPercent : CasinoPercent) / 100;
        Wagered += added;
        return added;
    }

    public WalletFailure? Close(BonusState state)
    {
        if (State != BonusState.Active || state == BonusState.Active)
        {
            return WalletFailure.InvalidState;
        }

        State = state;
        return null;
    }
}
