using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>Holds a stake (cash, then bonus money, or a free bet) or a withdrawal. Only the cash part counts against limits.</summary>
public sealed class ReserveFundsHandler(PostingRunner runner, IGamblingRules rules, TimeProvider time)
{
    public Task<WalletOutcome> HoldStakeAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference, StakeTerms? terms = null) =>
        runner.RunAsync(idempotencyKey, PostingKind.Reserve, accountId, amount, async transaction =>
        {
            if (await transaction.LockAccountAsync(accountId) is not { } account)
            {
                return PostingResult.Refuse(WalletFailure.AccountNotFound);
            }

            var stakeTerms = terms ?? StakeTerms.Unspecified;
            return stakeTerms.FreeBetId is { } freeBetId
                ? await HoldFreeBetAsync(transaction, account, freeBetId, idempotencyKey, amount, currency, reference, stakeTerms)
                : await HoldCashThenBonusAsync(transaction, account, idempotencyKey, amount, currency, reference, stakeTerms);
        });

    public Task<WalletOutcome> HoldWithdrawalAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        runner.RunAsync(idempotencyKey, PostingKind.WithdrawalHold, accountId, amount, async transaction =>
        {
            if (await transaction.LockAccountAsync(accountId) is not { } account)
            {
                return PostingResult.Refuse(WalletFailure.AccountNotFound);
            }

            if (account.Kind != AccountKind.Punter)
            {
                return PostingResult.Refuse(WalletFailure.InvalidState);
            }

            if (account.Reserve(amount, currency) is { } failure)
            {
                return PostingResult.Refuse(failure);
            }

            if (SpendPolicy.CheckWithdrawal(rules.For(account.UserId).In(account.Currency), time.GetUtcNow()) is { } blocked)
            {
                return PostingResult.Refuse(blocked);
            }

            List<LedgerEntry> entries = [new(accountId, Bucket.Available, -amount), new(accountId, Bucket.Reserved, amount)];

            // Bonus money cannot leave: asking to withdraw forfeits it in the same posting.
            if (await transaction.ActiveGrantAsync(accountId) is { } grant)
            {
                grant.Close(BonusState.Forfeited);
                await transaction.SaveGrantAsync(grant, isNew: false);
            }

            if (account.Bonus > 0 && WellKnownAccounts.PromotionsFor(currency) is { } promotions)
            {
                var forfeited = account.TakeBonus();
                entries.Add(new LedgerEntry(accountId, Bucket.Bonus, -forfeited));
                entries.Add(new LedgerEntry(promotions, Bucket.Available, forfeited));
            }

            var reservation = new Reservation(Guid.CreateVersion7(), accountId, amount, currency, reference, ReservationState.Held, ReservationPurpose.Withdrawal);
            return PostingResult.Hold(new Posting(Guid.CreateVersion7(), PostingKind.WithdrawalHold, idempotencyKey, reference, entries), account, reservation);
        });

    private async Task<PostingResult> HoldCashThenBonusAsync(
        IWalletTransaction transaction, Account account, string idempotencyKey, long amount, string currency, string reference, StakeTerms terms)
    {
        var now = time.GetUtcNow();
        var grant = account.Kind == AccountKind.Punter ? await transaction.ActiveGrantAsync(account.AccountId) : null;
        var liveGrant = grant is not null && grant.IsLive(now) ? grant : null;
        var (failure, fromBonus) = account.ReserveCashThenBonus(amount, currency, liveGrant?.Allows(terms.Product, terms.OddsHundredths) ?? false);
        if (failure is not null)
        {
            return PostingResult.Refuse(failure.Value);
        }

        var cash = amount - fromBonus;
        if (account.Kind == AccountKind.Punter && await JudgeStakeAsync(transaction, account, cash) is { } refused)
        {
            return refused;
        }

        if (liveGrant is not null)
        {
            await transaction.SaveStakeAsync(
                new BonusStake(idempotencyKey, account.AccountId, reference, terms.Product, terms.OddsHundredths, cash, fromBonus, liveGrant.GrantId, null, 0, 0), isNew: true);
        }

        List<LedgerEntry> entries = [new(account.AccountId, Bucket.Reserved, amount)];
        if (cash != 0)
        {
            entries.Add(new(account.AccountId, Bucket.Available, -cash));
        }

        if (fromBonus != 0)
        {
            entries.Add(new(account.AccountId, Bucket.Bonus, -fromBonus));
        }

        if (entries.Count < 2)
        {
            entries.Add(new(account.AccountId, Bucket.Available, 0));
        }

        var reservation = new Reservation(Guid.CreateVersion7(), account.AccountId, amount, currency, reference, ReservationState.Held, ReservationPurpose.Stake, fromBonus);
        return PostingResult.Hold(new Posting(Guid.CreateVersion7(), PostingKind.Reserve, idempotencyKey, reference, entries), account, reservation);
    }

    private async Task<PostingResult> HoldFreeBetAsync(
        IWalletTransaction transaction, Account account, Guid freeBetId, string idempotencyKey, long amount, string currency, string reference, StakeTerms terms)
    {
        if (account.Kind != AccountKind.Punter || terms.Product != StakeProduct.Sport || WellKnownAccounts.PromotionsFor(currency) is not { } promotions)
        {
            return PostingResult.Refuse(WalletFailure.InvalidState);
        }

        if (await transaction.LockFreeBetAsync(freeBetId) is not { } freeBet)
        {
            return PostingResult.Refuse(WalletFailure.InvalidState, "free bet not found");
        }

        if (freeBet.Use(account.AccountId, amount, terms.OddsHundredths, time.GetUtcNow()) is { } problem)
        {
            return PostingResult.Refuse(WalletFailure.InvalidState, problem);
        }

        if (account.HoldFreeBet(amount, currency) is { } failure)
        {
            return PostingResult.Refuse(failure);
        }

        // Blocks still apply to a free bet, but its stake is not the customer's money, so it adds nothing to their limits.
        if (await JudgeStakeAsync(transaction, account, 0) is { } refused)
        {
            return refused;
        }

        await transaction.SaveFreeBetAsync(freeBet, isNew: false);
        await transaction.SaveStakeAsync(new BonusStake(idempotencyKey, account.AccountId, reference, StakeProduct.Sport, terms.OddsHundredths, 0, 0, null, freeBetId, amount, 0), isNew: true);
        var reservation = new Reservation(Guid.CreateVersion7(), account.AccountId, amount, currency, reference, ReservationState.Held, ReservationPurpose.Stake, 0, freeBetId);
        return PostingResult.Hold(new Posting(Guid.CreateVersion7(), PostingKind.Reserve, idempotencyKey, reference,
            [new(promotions, Bucket.Available, -amount), new(account.AccountId, Bucket.Reserved, amount)]), account, reservation);
    }

    private async Task<PostingResult?> JudgeStakeAsync(IWalletTransaction transaction, Account account, long cash)
    {
        var now = time.GetUtcNow();
        if (SpendPolicy.CheckStake(rules.For(account.UserId).In(account.Currency), await transaction.GetSpendAsync(account.AccountId, now), cash, now) is { } refusal)
        {
            return PostingResult.Refuse(refusal, LimitHit.From(refusal, account, "stake", cash));
        }

        await transaction.AddSpendAsync(account.AccountId, now, cash, 0, 0);
        return null;
    }
}
