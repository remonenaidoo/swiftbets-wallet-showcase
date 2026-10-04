using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Ports;

public interface IPromotionStore
{
    Task<IReadOnlyList<PromotionRecord>> ListAsync(CancellationToken cancellationToken);

    Task<PromotionRecord?> GetAsync(Guid promotionId, CancellationToken cancellationToken);

    /// <summary>Saves a new or changed offer and its audit entry in one transaction.</summary>
    Task SaveAsync(Promotion promotion, bool isNew, PromotionAuditEntry audit, CancellationToken cancellationToken);

    Task<IReadOnlyList<PromotionAuditEntry>> AuditAsync(Guid promotionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PromotionAward>> AwardsAsync(Guid promotionId, CancellationToken cancellationToken);

    /// <summary>Offers of a kind and currency running at <paramref name="at"/>.</summary>
    Task<IReadOnlyList<Promotion>> RunningAsync(PromotionKind kind, string currency, DateTimeOffset at, CancellationToken cancellationToken);

    Task OptInAsync(Guid promotionId, Guid userId, DateTimeOffset at, CancellationToken cancellationToken);

    Task OptOutAsync(Guid promotionId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> OptedInAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Offers the customer has been awarded.</summary>
    Task<IReadOnlySet<Guid>> AwardedAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> MarketingOptedOutAsync(Guid userId, CancellationToken cancellationToken);

    Task SetMarketingOptedOutAsync(Guid userId, bool optedOut, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Records a decision; false when this trigger was already decided, or the customer already has the award.</summary>
    Task<bool> RecordAwardAsync(PromotionAward award, CancellationToken cancellationToken);

    /// <summary>Records the award and issues its free bet together; false (and no free bet) when it was already awarded.</summary>
    Task<bool> AwardFreeBetAsync(PromotionAward award, FreeBet freeBet, CancellationToken cancellationToken);

    /// <summary>Whether the reference is the customer's earliest captured stake, so its settlement is their first bet's.</summary>
    Task<bool> IsFirstStakeAsync(Guid userId, string reference, CancellationToken cancellationToken);
}

public sealed record PromotionRecord(Promotion Promotion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int AwardCount);

public sealed record PromotionAuditEntry(Guid PromotionId, string Actor, string Action, string Detail, DateTimeOffset At);

public enum AwardOutcome : byte
{
    Awarded = 1,
    Refused = 2,
}

public sealed record PromotionAward(Guid AwardId, Guid PromotionId, Guid UserId, string TriggerKey, AwardOutcome Outcome, string? Reason, long Amount, DateTimeOffset AwardedAt);
