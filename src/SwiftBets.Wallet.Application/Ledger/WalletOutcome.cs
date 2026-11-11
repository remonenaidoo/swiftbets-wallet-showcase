using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>Detail says why a refusal happened when the failure alone does not, such as which limit and what is left.</summary>
public sealed record WalletOutcome(bool WasApplied, WalletFailure? Failure, Guid? PostingId, Account? Account, Reservation? Reservation, string? Detail = null)
{
    public static WalletOutcome Failed(WalletFailure failure, string? detail = null) => new(false, failure, null, null, null, detail);
}

/// <summary>A stake or deposit refused at one of the customer's own limits.</summary>
public sealed record LimitHit(Guid UserId, SpendLimit Limit, string Refused, long Attempted, string Currency)
{
    public static LimitHit? From(SpendRefusal refusal, Account account, string refused, long attempted) =>
        refusal.Limit is { } limit ? new LimitHit(account.UserId, limit, refused, attempted, account.Currency) : null;
}
