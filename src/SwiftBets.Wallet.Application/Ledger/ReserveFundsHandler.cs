using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>
/// Holds money: a stake, judged against stake and loss limits and betting blocks under the account lock (ADR 0006), or a
/// withdrawal, refused only by a withdrawal block and kept out of the spend totals. A stake takes cash first, then bonus
/// money where the active offer allows the product and price; a free bet's stake comes from the promotions account
/// (D166). Only the cash part counts against the customer's limits. A withdrawal forfeits the bonus balance.
/// </summary>
public sealed class ReserveFundsHandler(PostingRunner runner, IGamblingRules rules, TimeProvider time)
{
    public Task<WalletOutcome> HandleAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference, StakeTerms? terms = null) =>
        HoldAsync(ReservationPurpose.Stake, idempotencyKey, accountId, amount, currency, reference, terms ?? StakeTerms.Unspecified);

    public Task<WalletOutcome> HoldWithdrawalAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        HoldAsync(ReservationPurpose.Withdrawal, idempotencyKey, accountId, amount, currency, reference, StakeTerms.Unspecified);

    private Task<WalletOutcome> HoldAsync(ReservationPurpose purpose, string idempotencyKey, Guid accountId, long amount, string currency, string reference, StakeTerms terms)
    {
        var kind = purpose == ReservationPurpose.Stake ? PostingKind.Reserve : PostingKind.WithdrawalHold;
        return runner.RunAsync(idempotencyKey, kind, accountId, amount, async transaction =>
        {
            var account = await transaction.LockAccountAsync(accountId);
            if (account is null)
            {
                return (WalletFailure.AccountNotFound, null, [], null, false);
            }

            if (purpose == ReservationPurpose.Withdrawal)
            {
                return await HoldWithdrawalAsync(transaction, account, idempotencyKey, amount, currency, reference);
            }

            if (terms.FreeBetId is { } freeBetId)
            {
                return await HoldFreeBetAsync(transaction, account, freeBetId, idempotencyKey, amount, currency, reference, terms);
            }

            var now = time.GetUtcNow();
            var grant = account.Kind == AccountKind.Punter ? await transaction.ActiveGrantAsync(accountId) : null;
            var usable = grant is not null && grant.IsLive(now) && grant.Allows(terms.Product, terms.OddsHundredths);
            var (failure, fromBonus) = account.ReserveCashThenBonus(amount, currency, usable);
            if (failure is not null)
            {
                return (failure, null, [], null, false);
            }

            if (account.Kind == AccountKind.Punter)
            {
                await JudgeStakeAsync(transaction, account, amount - fromBonus);
            }

            if (grant is not null && grant.IsLive(now))
            {
                await transaction.SaveStakeAsync(new BonusStake(idempotencyKey, accountId, reference, terms.Product, terms.OddsHundredths, amount - fromBonus, fromBonus, grant.GrantId, null, 0, 0), isNew: true);
            }

            var reservation = new Reservation(Guid.CreateVersion7(), accountId, amount, currency, reference, ReservationState.Held, ReservationPurpose.Stake, fromBonus);
            List<LedgerEntry> entries = [new(accountId, Bucket.Reserved, amount)];
            AddIfMoved(entries, accountId, Bucket.Available, -(amount - fromBonus));
            AddIfMoved(entries, accountId, Bucket.Bonus, -fromBonus);
            if (entries.Count < 2)
            {
                entries.Add(new LedgerEntry(accountId, Bucket.Available, 0));
            }

            return (null, new Posting(Guid.CreateVersion7(), kind, idempotencyKey, reference, entries), [account], reservation, true);
        });
    }

    private async Task<(WalletFailure?, Posting?, IReadOnlyList<Account>, Reservation?, bool)> HoldWithdrawalAsync(
        IWalletTransaction transaction, Account account, string idempotencyKey, long amount, string currency, string reference)
    {
        if (account.Kind != AccountKind.Punter)
        {
            return (WalletFailure.InvalidState, null, [], null, false);
        }

        if (account.Reserve(amount, currency) is { } failure)
        {
            return (failure, null, [], null, false);
        }

        if (SpendPolicy.CheckWithdrawal(rules.For(account.UserId).In(account.Currency), time.GetUtcNow()) is { } blocked)
        {
            throw new WalletRefusedException(blocked.Failure, blocked.Detail);
        }

        List<LedgerEntry> entries = [new(account.AccountId, Bucket.Available, -amount), new(account.AccountId, Bucket.Reserved, amount)];

        // Bonus money cannot leave: asking to withdraw forfeits it (the customer is warned first) in the same posting.
        if (await transaction.ActiveGrantAsync(account.AccountId) is { } grant)
        {
            grant.Close(BonusState.Forfeited);
            await transaction.SaveGrantAsync(grant, isNew: false);
        }

        if (account.Bonus > 0 && WellKnownAccounts.PromotionsFor(currency) is { } promotions)
        {
            var forfeited = account.TakeBonus();
            entries.Add(new LedgerEntry(account.AccountId, Bucket.Bonus, -forfeited));
            entries.Add(new LedgerEntry(promotions, Bucket.Available, forfeited));
        }

        var reservation = new Reservation(Guid.CreateVersion7(), account.AccountId, amount, currency, reference, ReservationState.Held, ReservationPurpose.Withdrawal);
        return (null, new Posting(Guid.CreateVersion7(), PostingKind.WithdrawalHold, idempotencyKey, reference, entries), [account], reservation, true);
    }

    private async Task<(WalletFailure?, Posting?, IReadOnlyList<Account>, Reservation?, bool)> HoldFreeBetAsync(
        IWalletTransaction transaction, Account account, Guid freeBetId, string idempotencyKey, long amount, string currency, string reference, StakeTerms terms)
    {
        if (account.Kind != AccountKind.Punter || terms.Product != StakeProduct.Sport || WellKnownAccounts.PromotionsFor(currency) is not { } promotions)
        {
            return (WalletFailure.InvalidState, null, [], null, false);
        }

        var freeBet = await transaction.LockFreeBetAsync(freeBetId);
        if (freeBet is null)
        {
            throw new WalletRefusedException(WalletFailure.InvalidState, "free bet not found");
        }

        if (freeBet.Use(account.AccountId, amount, terms.OddsHundredths, time.GetUtcNow()) is { } problem)
        {
            throw new WalletRefusedException(WalletFailure.InvalidState, problem);
        }

        if (account.HoldFreeBet(amount, currency) is { } failure)
        {
            return (failure, null, [], null, false);
        }

        // Blocks still apply to a free bet; its stake is not the customer's money, so it adds nothing to their limits.
        await JudgeStakeAsync(transaction, account, 0);
        await transaction.SaveFreeBetAsync(freeBet, isNew: false);
        await transaction.SaveStakeAsync(new BonusStake(idempotencyKey, account.AccountId, reference, StakeProduct.Sport, terms.OddsHundredths, 0, 0, null, freeBetId, amount, 0), isNew: true);
        var reservation = new Reservation(Guid.CreateVersion7(), account.AccountId, amount, currency, reference, ReservationState.Held, ReservationPurpose.Stake, 0, freeBetId);
        return (null, new Posting(Guid.CreateVersion7(), PostingKind.Reserve, idempotencyKey, reference,
            [new(promotions, Bucket.Available, -amount), new(account.AccountId, Bucket.Reserved, amount)]), [account], reservation, true);
    }

    private async Task JudgeStakeAsync(IWalletTransaction transaction, Account account, long cash)
    {
        var now = time.GetUtcNow();
        if (SpendPolicy.CheckStake(rules.For(account.UserId).In(account.Currency), await transaction.GetSpendAsync(account.AccountId, now), cash, now) is { } refusal)
        {
            throw new WalletRefusedException(refusal.Failure, refusal.Detail, refusal.Limit is { } hit ? new LimitHit(account.UserId, hit, "stake", cash, account.Currency) : null);
        }

        await transaction.AddSpendAsync(account.AccountId, now, cash, 0, 0);
    }

    private static void AddIfMoved(List<LedgerEntry> entries, Guid accountId, Bucket bucket, long amount)
    {
        if (amount != 0)
        {
            entries.Add(new LedgerEntry(accountId, bucket, amount));
        }
    }
}
