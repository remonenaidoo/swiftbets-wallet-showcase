using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Promotions;

/// <summary>
/// Responsible gambling comes before offers: a self-excluded, cooling-off or betting-blocked customer sees no offers and
/// gets no awards, and a customer who turned offers off gets none either.
/// </summary>
public sealed class OfferEligibility(IGamblingRules rules, IPromotionStore store, TimeProvider time)
{
    public bool IsRestricted(Guid userId) =>
        rules.For(userId).Blocks.Any(b => b.Kind is BlockKind.Exclusion or BlockKind.NoBetting && b.IsActive(time.GetUtcNow()));

    /// <summary>Why the customer may get no offer now, or null.</summary>
    public async Task<string?> RefusalAsync(Guid userId, CancellationToken cancellationToken) =>
        IsRestricted(userId) ? "account restricted"
        : await store.MarketingOptedOutAsync(userId, cancellationToken) ? "offers turned off"
        : null;
}
