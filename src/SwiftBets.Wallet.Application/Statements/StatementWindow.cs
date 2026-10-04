using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Application.Statements;

/// <summary>
/// The bounds of a customer statement (D196): a statement always covers a date range, never all time. With no dates it is
/// the last 30 days; a range may span at most 12 months, and an immediate CSV download at most 90 days.
/// </summary>
public static class StatementWindow
{
    public const int DefaultDays = 30;
    public const int MaxMonths = 12;
    public const int MaxCsvDays = 90;

    /// <summary>The statement's types and the ledger posting kinds each one covers.</summary>
    public static readonly IReadOnlyDictionary<string, PostingKind[]> Types = new Dictionary<string, PostingKind[]>(StringComparer.Ordinal)
    {
        ["bets"] = [PostingKind.Reserve, PostingKind.Release],
        ["wins"] = [PostingKind.Credit],
        ["deposits"] = [PostingKind.Deposit, PostingKind.TopUp, PostingKind.VoucherRedeem],
        ["withdrawals"] = [PostingKind.WithdrawalHold, PostingKind.WithdrawalReturned],
        ["bonus"] = [PostingKind.BonusGranted, PostingKind.BonusConverted, PostingKind.BonusForfeited],
    };

    /// <summary>Fills missing ends (to = now, from = to - 30 days) and checks the range; Problem is set when it is refused.</summary>
    public static (DateTimeOffset From, DateTimeOffset To, string? Problem) Resolve(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
    {
        var end = to ?? now;
        var start = from ?? end.AddDays(-DefaultDays);
        if (end <= start)
        {
            return (start, end, "to must be after from.");
        }

        return start.AddMonths(MaxMonths) < end ? (start, end, $"A statement covers at most {MaxMonths} months. Pick a shorter range.") : (start, end, null);
    }

    /// <summary>The posting kinds for a type; null for all types; false when the type is unknown.</summary>
    public static bool TryKinds(string? type, out IReadOnlyCollection<PostingKind>? kinds)
    {
        kinds = null;
        if (string.IsNullOrEmpty(type))
        {
            return true;
        }

        if (Types.TryGetValue(type, out var found))
        {
            kinds = found;
            return true;
        }

        return false;
    }

    /// <summary>Whether a range is short enough for an immediate CSV download.</summary>
    public static bool FitsImmediateCsv(DateTimeOffset from, DateTimeOffset to) => to - from <= TimeSpan.FromDays(MaxCsvDays);
}
