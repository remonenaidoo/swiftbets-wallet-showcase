using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Compliance;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Infrastructure.ResponsibleGambling;

/// <summary>The wallet reports unready until compliance's topic is read to its end, so it never judges against rules it has not seen.</summary>
public sealed class CompactedGamblingRules(ICompactedState<RestrictionsChangedV1> state) : IGamblingRules
{
    public GamblingRules For(Guid userId) =>
        state.TryGet(userId.ToString(), out var snapshot) ? Map(snapshot) : GamblingRules.None;

    public static GamblingRules Map(RestrictionsChangedV1 snapshot) => new(
        [.. snapshot.Limits.Select(l => new SpendLimit(
            l.Kind switch { LimitKind.Deposit => SpendKind.Deposit, LimitKind.Stake => SpendKind.Stake, _ => SpendKind.Loss },
            l.Period switch { LimitPeriod.Day => SpendPeriod.Day, LimitPeriod.Week => SpendPeriod.Week, _ => SpendPeriod.Month },
            l.Amount.MinorUnits,
            l.PendingAmount?.MinorUnits,
            l.PendingEffectiveAt,
            l.Amount.Currency))],
        [.. snapshot.Restrictions.Select(Block).OfType<SpendBlock>()]);

    private static SpendBlock? Block(Restriction restriction) =>
        restriction.Kind switch
        {
            RestrictionKind.CoolingOff or RestrictionKind.SelfExclusion => new SpendBlock(BlockKind.Exclusion, restriction.StartsAt, restriction.EndsAt),
            RestrictionKind.NoDeposits => new SpendBlock(BlockKind.NoDeposits, restriction.StartsAt, restriction.EndsAt),
            RestrictionKind.NoBetting => new SpendBlock(BlockKind.NoBetting, restriction.StartsAt, restriction.EndsAt),
            RestrictionKind.NoWithdrawals => new SpendBlock(BlockKind.NoWithdrawals, restriction.StartsAt, restriction.EndsAt),
            _ => null,
        };
}
