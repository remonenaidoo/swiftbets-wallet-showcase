using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>
/// Ends a held reservation: a stake is captured into the house, a withdrawal is paid out to the funding account, and
/// either can be released back to the punter. Capturing a withdrawal as a stake (or the reverse) is refused. A captured
/// stake counts towards the active bonus's wagering; a released one goes back the way it was paid (D166).
/// </summary>
public sealed class SettleReservationHandler(PostingRunner runner, IWalletStore store, BonusLedgerHandler? bonuses = null)
{
    public Task<WalletOutcome> CaptureAsync(string idempotencyKey, Guid reservationId, CancellationToken cancellationToken) =>
        RunAsync(idempotencyKey, reservationId, Ending.Capture, cancellationToken);

    public Task<WalletOutcome> CompleteWithdrawalAsync(string idempotencyKey, Guid reservationId, CancellationToken cancellationToken) =>
        RunAsync(idempotencyKey, reservationId, Ending.PayOut, cancellationToken);

    public Task<WalletOutcome> ReleaseAsync(string idempotencyKey, Guid reservationId, CancellationToken cancellationToken) =>
        RunAsync(idempotencyKey, reservationId, Ending.Release, cancellationToken);

    private enum Ending
    {
        Capture,
        PayOut,
        Release,
    }

    private async Task<WalletOutcome> RunAsync(string idempotencyKey, Guid reservationId, Ending ending, CancellationToken cancellationToken)
    {
        var known = await store.GetReservationAsync(reservationId, cancellationToken);
        if (known is null)
        {
            return WalletOutcome.Failed(WalletFailure.ReservationNotFound);
        }

        var isWithdrawal = known.Purpose == ReservationPurpose.Withdrawal;
        if ((ending == Ending.Capture && isWithdrawal) || (ending == Ending.PayOut && !isWithdrawal))
        {
            return WalletOutcome.Failed(WalletFailure.InvalidState);
        }

        if ((ending == Ending.Capture ? WellKnownAccounts.HouseFor(known.Currency) : WellKnownAccounts.FundingFor(known.Currency)) is not { } counterparty)
        {
            return WalletOutcome.Failed(WalletFailure.CurrencyMismatch);
        }

        var kind = ending switch
        {
            Ending.Capture => PostingKind.Capture,
            Ending.PayOut => PostingKind.WithdrawalPaid,
            _ => isWithdrawal ? PostingKind.WithdrawalReturned : PostingKind.Release,
        };
        var outcome = await runner.RunAsync(idempotencyKey, kind, known.AccountId, known.Amount, async transaction =>
        {
            var reservation = (await transaction.LockReservationAsync(reservationId))!;
            var account = (await transaction.LockAccountAsync(reservation.AccountId))!;
            var release = ending == Ending.Release;
            var (toBonus, forfeit) = release ? await BonusReturnAsync(transaction, reservation) : (0, 0);
            var toPromotions = (reservation.FreeBetId is null ? 0 : reservation.Amount) + forfeit;
            var failure = reservation.MoveTo(release ? ReservationState.Released : ReservationState.Captured)
                ?? (release ? account.ReturnReservation(reservation.Amount, toBonus, toPromotions) : account.ConsumeReservation(reservation.Amount));
            if (failure is not null)
            {
                return (failure, null, [], null, false);
            }

            if (!release && !isWithdrawal)
            {
                await CountWageringAsync(transaction, reservation);
            }
            else if (release && reservation.FreeBetId is { } freeBetId && await transaction.LockFreeBetAsync(freeBetId) is { } freeBet)
            {
                freeBet.Restore();
                await transaction.SaveFreeBetAsync(freeBet, isNew: false);
            }

            // A released stake was never at risk, so it comes off the totals of the periods it was placed in.
            if (release && !isWithdrawal && account.Kind == AccountKind.Punter)
            {
                await transaction.AddSpendAsync(account.AccountId, await transaction.ReservedAtAsync(reservationId), -(reservation.Amount - reservation.BonusAmount - (reservation.FreeBetId is null ? 0 : reservation.Amount)), 0, 0);
            }

            List<LedgerEntry> entries = [new(account.AccountId, Bucket.Reserved, -reservation.Amount)];
            if (!release)
            {
                entries.Add(new(counterparty, Bucket.Available, reservation.Amount));
            }
            else
            {
                var cash = reservation.Amount - toBonus - toPromotions;
                if (cash != 0 || entries.Count + (toBonus != 0 ? 1 : 0) + (toPromotions != 0 ? 1 : 0) < 2)
                {
                    entries.Add(new(account.AccountId, Bucket.Available, cash));
                }

                if (toBonus != 0)
                {
                    entries.Add(new(account.AccountId, Bucket.Bonus, toBonus));
                }

                if (toPromotions != 0)
                {
                    entries.Add(new(WellKnownAccounts.PromotionsFor(known.Currency)!.Value, Bucket.Available, toPromotions));
                }
            }

            return (null, new Posting(Guid.CreateVersion7(), kind, idempotencyKey, reservation.Reference, entries), [account], reservation, false);
        });

        if (outcome.WasApplied && ending == Ending.Capture && bonuses is not null)
        {
            await bonuses.ConvertIfMetAsync(known.AccountId);
        }

        return outcome;
    }

    /// <summary>
    /// How much of a released stake goes back to bonus: all its bonus part while that offer is live, into cash once the
    /// offer converted, and to the promotions account if the offer was forfeited or expired meanwhile.
    /// </summary>
    private static async Task<(long ToBonus, long Forfeit)> BonusReturnAsync(IWalletTransaction transaction, Reservation reservation)
    {
        if (reservation.BonusAmount == 0)
        {
            return (0, 0);
        }

        var stake = await transaction.FindStakeAsync(reservation.AccountId, reservation.Reference);
        var grant = stake?.GrantId is { } grantId ? await transaction.GetGrantAsync(grantId) : null;
        return grant?.State switch
        {
            BonusState.Active => (reservation.BonusAmount, 0),
            BonusState.Converted => (0, 0),
            _ => (0, reservation.BonusAmount),
        };
    }

    private static async Task CountWageringAsync(IWalletTransaction transaction, Reservation reservation)
    {
        if (await transaction.FindStakeAsync(reservation.AccountId, reservation.Reference) is not { FreeBetId: null } stake
            || await transaction.ActiveGrantAsync(reservation.AccountId) is not { } grant || grant.GrantId != stake.GrantId)
        {
            return;
        }

        if (grant.Contribute(stake.CashAmount + stake.BonusAmount, stake.Product, stake.OddsHundredths) > 0)
        {
            await transaction.SaveGrantAsync(grant, isNew: false);
        }
    }
}
