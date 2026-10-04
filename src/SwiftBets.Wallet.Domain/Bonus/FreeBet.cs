namespace SwiftBets.Wallet.Domain.Bonus;

/// <summary>A token worth one stake of its amount at or above its minimum odds. A win pays the return without the stake.</summary>
public sealed class FreeBet(Guid freeBetId, Guid accountId, Guid? promotionId, long amount, int minOddsHundredths, DateTimeOffset expiresAt, FreeBetState state)
{
    public Guid FreeBetId { get; } = freeBetId;

    public Guid AccountId { get; } = accountId;

    public Guid? PromotionId { get; } = promotionId;

    public long Amount { get; } = amount;

    public int MinOddsHundredths { get; } = minOddsHundredths;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public FreeBetState State { get; private set; } = state;

    /// <summary>Spends the token on a stake; returns why it cannot be, or null.</summary>
    public string? Use(Guid accountId, long stake, int oddsHundredths, DateTimeOffset now)
    {
        var problem = accountId != AccountId ? "free bet not found"
            : State != FreeBetState.Available || ExpiresAt <= now ? "free bet already used or expired"
            : stake != Amount ? "stake must equal the free bet"
            : oddsHundredths < MinOddsHundredths ? "odds below the free bet minimum"
            : null;
        if (problem is null)
        {
            State = FreeBetState.Used;
        }

        return problem;
    }

    /// <summary>A released stake gives the token back.</summary>
    public void Restore()
    {
        if (State == FreeBetState.Used)
        {
            State = FreeBetState.Available;
        }
    }
}
