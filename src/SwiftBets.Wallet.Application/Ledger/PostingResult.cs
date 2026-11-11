using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>What a posting step decided: a balanced posting to store, or why the movement is refused.</summary>
public sealed record PostingResult
{
    private PostingResult()
    {
    }

    public Posting? Posting { get; private init; }

    public IReadOnlyList<Account> Changed { get; private init; } = [];

    public Reservation? Reservation { get; private init; }

    public bool IsNewReservation { get; private init; }

    public WalletFailure? Failure { get; private init; }

    public string? Detail { get; private init; }

    public LimitHit? Limit { get; private init; }

    public static PostingResult Post(Posting posting, IReadOnlyList<Account> changed, Reservation? reservation = null) =>
        new() { Posting = posting, Changed = changed, Reservation = reservation };

    public static PostingResult Hold(Posting posting, Account changed, Reservation reservation) =>
        new() { Posting = posting, Changed = [changed], Reservation = reservation, IsNewReservation = true };

    public static PostingResult Refuse(WalletFailure failure, string? detail = null, LimitHit? limit = null) =>
        new() { Failure = failure, Detail = detail, Limit = limit };

    public static PostingResult Refuse(SpendRefusal refusal, LimitHit? limit = null) => Refuse(refusal.Failure, refusal.Detail, limit);
}
