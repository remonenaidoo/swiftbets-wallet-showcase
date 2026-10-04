using System.Text.Json;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Promotions;

/// <summary>The console's promotion builder; every save is audited with who made it and the offer as saved.</summary>
public sealed class PromotionAdminHandler(IPromotionStore store, TimeProvider time)
{
    public async Task<(PromotionRecord? Saved, string? Problem)> SaveAsync(Guid? promotionId, Promotion draft, string actor, CancellationToken cancellationToken)
    {
        var existing = promotionId is { } id ? await store.GetAsync(id, cancellationToken) : null;
        if (promotionId is not null && existing is null)
        {
            return (null, null);
        }

        var promotion = draft with { PromotionId = promotionId ?? Guid.CreateVersion7(), Name = draft.Name.Trim(), Terms = draft.Terms.Trim() };
        if (promotion.Problem() is { } problem)
        {
            return (null, problem);
        }

        var now = time.GetUtcNow();
        await store.SaveAsync(promotion, existing is null, new PromotionAuditEntry(promotion.PromotionId, actor, existing is null ? "created" : "updated",
            JsonSerializer.Serialize(promotion, JsonSerializerOptions.Web), now), cancellationToken);
        return (await store.GetAsync(promotion.PromotionId, cancellationToken), null);
    }
}
