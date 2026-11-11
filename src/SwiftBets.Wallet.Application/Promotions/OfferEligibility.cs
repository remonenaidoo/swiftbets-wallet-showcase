using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Promotions;

/// <summary>A blocked or excluded customer, or one who turned offers off, gets no offers.</summary>
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
