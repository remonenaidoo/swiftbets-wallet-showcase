using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Ports;

/// <summary>Reads of offers' money outside a posting; every change is made under the account lock in a posting.</summary>
public interface IBonusStore
{
    Task<BonusGrant?> ActiveGrantAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FreeBet>> AvailableFreeBetsAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Active grants that have expired or whose wagering is met, oldest first.</summary>
    Task<IReadOnlyList<BonusGrant>> GrantsDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Punter accounts holding bonus money with no active grant to own it (a stake returned after its offer ended).</summary>
    Task<IReadOnlyList<OrphanBonus>> OrphanBonusAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Marks free bets past their expiry as expired; returns how many.</summary>
    Task<int> ExpireFreeBetsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record OrphanBonus(Guid AccountId, string Currency, long Bonus);
