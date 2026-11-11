using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>
/// The one way money moves. The idempotency key's unique index is the arbiter: a request that loses the race rolls back
/// and replays the winner. Writes ignore cancellation, since stopping between legs would leave money half-moved.
/// </summary>
public sealed class PostingRunner(IWalletStore store, ILimitReachedNotifier? limits = null)
{
    public async Task<WalletOutcome> RunAsync(string idempotencyKey, PostingKind kind, Guid accountId, long amount, Func<IWalletTransaction, Task<PostingResult>> apply)
    {
        (WalletOutcome Outcome, LimitHit? Limit) attempt;
        try
        {
            attempt = await AttemptAsync(idempotencyKey, kind, accountId, amount, apply);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            attempt = await AttemptAsync(idempotencyKey, kind, accountId, amount, apply);
        }

        if (attempt.Limit is { } hit && limits is not null)
        {
            await limits.NotifyAsync(hit);
        }

        return attempt.Outcome;
    }

    private async Task<(WalletOutcome Outcome, LimitHit? Limit)> AttemptAsync(
        string idempotencyKey, PostingKind kind, Guid accountId, long amount, Func<IWalletTransaction, Task<PostingResult>> apply)
    {
        await using var transaction = await store.BeginAsync();
        if (await transaction.FindPostingAsync(idempotencyKey) is { } existing)
        {
            return (await ReplayAsync(transaction, existing, kind, accountId, amount), null);
        }

        var result = await apply(transaction);
        if (result.Failure is { } failure)
        {
            return (WalletOutcome.Failed(failure, result.Detail), result.Limit);
        }

        var posting = result.Posting!;
        await transaction.SaveAsync(
            posting,
            new StoredPosting(posting.PostingId, kind, accountId, amount, result.Reservation?.ReservationId),
            result.Changed,
            result.Reservation,
            result.IsNewReservation);
        await transaction.CommitAsync();
        return (new WalletOutcome(true, null, posting.PostingId, result.Changed.FirstOrDefault(a => a.AccountId == accountId), result.Reservation), null);
    }

    private static async Task<WalletOutcome> ReplayAsync(IWalletTransaction transaction, StoredPosting existing, PostingKind kind, Guid accountId, long amount)
    {
        if (existing.Kind != kind || existing.AccountId != accountId || existing.Amount != amount)
        {
            return WalletOutcome.Failed(WalletFailure.IdempotencyConflict);
        }

        var account = await transaction.LockAccountAsync(existing.AccountId);
        var reservation = existing.ReservationId is { } id ? await transaction.LockReservationAsync(id) : null;
        return new WalletOutcome(false, null, existing.PostingId, account, reservation);
    }
}
