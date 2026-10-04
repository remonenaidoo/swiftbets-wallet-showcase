namespace SwiftBets.Wallet.Domain.ResponsibleGambling;

/// <summary>Why money may not move; <c>Limit</c> is the customer's own limit that refused it, when one did.</summary>
public sealed record SpendRefusal(WalletFailure Failure, string Detail, SpendLimit? Limit = null);

/// <summary>
/// Judged under the account lock against the period totals (ADR 0006). A stake counts in full against the loss limit,
/// because it may all be lost; winnings earn the headroom back.
/// </summary>
public static class SpendPolicy
{
    public static SpendRefusal? CheckStake(GamblingRules rules, SpendCounters counters, long amount, DateTimeOffset now)
    {
        if (Blocked(rules, now, BlockKind.Exclusion, BlockKind.NoBetting) is { } refusal)
        {
            return refusal;
        }

        return Exceeds(rules, counters, SpendKind.Stake, amount, now, t => t.Staked)
            ?? Exceeds(rules, counters, SpendKind.Loss, amount, now, t => t.Loss);
    }

    public static SpendRefusal? CheckDeposit(GamblingRules rules, SpendCounters counters, long amount, DateTimeOffset now) =>
        Blocked(rules, now, BlockKind.Exclusion, BlockKind.NoDeposits)
        ?? Exceeds(rules, counters, SpendKind.Deposit, amount, now, t => t.Deposited);

    /// <summary>Only an operator's withdrawal block stops a withdrawal; an excluded customer can always take their money out.</summary>
    public static SpendRefusal? CheckWithdrawal(GamblingRules rules, DateTimeOffset now) =>
        Blocked(rules, now, BlockKind.NoWithdrawals);

    /// <summary>A voucher is betting money for someone, so exclusion and betting or withdrawal blocks all stop buying one.</summary>
    public static SpendRefusal? CheckVoucherPurchase(GamblingRules rules, DateTimeOffset now) =>
        Blocked(rules, now, BlockKind.Exclusion, BlockKind.NoBetting, BlockKind.NoWithdrawals);

    private static SpendRefusal? Blocked(GamblingRules rules, DateTimeOffset now, params BlockKind[] kinds) =>
        rules.Blocks.FirstOrDefault(b => kinds.Contains(b.Kind) && b.IsActive(now)) is { } block
            ? new SpendRefusal(WalletFailure.AccountRestricted, block.Kind == BlockKind.Exclusion ? "account excluded" : $"account blocked: {block.Kind}")
            : null;

    private static SpendRefusal? Exceeds(GamblingRules rules, SpendCounters counters, SpendKind kind, long amount, DateTimeOffset now, Func<SpendTotals, long> used)
    {
        foreach (var limit in rules.Limits.Where(l => l.Kind == kind).OrderBy(l => l.Period))
        {
            if (limit.CapAt(now) is { } cap && used(counters.For(limit.Period)) + amount > cap)
            {
                var left = Math.Max(0, cap - used(counters.For(limit.Period)));
                return new SpendRefusal(WalletFailure.LimitExceeded, $"{kind} limit per {limit.Period}".ToLowerInvariant() + $": {left} left", limit);
            }
        }

        return null;
    }
}
