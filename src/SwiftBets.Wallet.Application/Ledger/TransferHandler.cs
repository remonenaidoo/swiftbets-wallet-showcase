using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>Credits, deposits, top-ups and debits between a punter account and the house or funding account of its currency.</summary>
public sealed class TransferHandler(PostingRunner runner, IGamblingRules rules, TimeProvider time, BonusLedgerHandler bonuses)
{
    /// <summary>Only casino stake debits carry this reason, and only they may be paid with bonus money.</summary>
    public const string CasinoBetReason = "casino-bet";

    public Task<WalletOutcome> CreditAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        MoveAsync(PostingKind.Credit, idempotencyKey, accountId, amount, currency, reference);

    /// <summary>An operator top-up also opens a customer's first account, whose id is the user id.</summary>
    public Task<WalletOutcome> TopUpAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        MoveAsync(PostingKind.TopUp, idempotencyKey, accountId, amount, currency, reference, openIfMissing: true);

    public Task<WalletOutcome> DepositAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        MoveAsync(PostingKind.Deposit, idempotencyKey, accountId, amount, currency, reference);

    public async Task<WalletOutcome> DebitAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference, string? reason = null)
    {
        if (reason != CasinoBetReason)
        {
            return await ClawBackAsync(idempotencyKey, accountId, amount, currency, reference);
        }

        var outcome = await CasinoStakeAsync(idempotencyKey, accountId, amount, currency, reference);
        if (outcome.WasApplied)
        {
            await bonuses.ConvertIfMetAsync(accountId);
        }

        return outcome;
    }

    private Task<WalletOutcome> CasinoStakeAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        runner.RunAsync(idempotencyKey, PostingKind.Debit, accountId, amount, async transaction =>
        {
            if (await transaction.LockAccountAsync(accountId) is not { } account)
            {
                return PostingResult.Refuse(WalletFailure.AccountNotFound);
            }

            if (WellKnownAccounts.HouseFor(currency) is not { } house)
            {
                return PostingResult.Refuse(WalletFailure.CurrencyMismatch);
            }

            var now = time.GetUtcNow();
            var grant = account.Kind == AccountKind.Punter ? await transaction.ActiveGrantAsync(accountId) : null;
            var liveGrant = grant is not null && grant.IsLive(now) ? grant : null;
            var (failure, fromBonus) = account.PayCashThenBonus(amount, currency, liveGrant?.Allows(StakeProduct.Casino, 0) ?? false);
            if (failure is not null)
            {
                return PostingResult.Refuse(failure.Value);
            }

            var cash = amount - fromBonus;
            if (account.Kind == AccountKind.Punter)
            {
                await transaction.AddSpendAsync(accountId, now, 0, -cash, 0);
                await transaction.SaveStakeAsync(new BonusStake(idempotencyKey, accountId, reference, StakeProduct.Casino, 0, cash, fromBonus, liveGrant?.GrantId, null, 0, 0), isNew: true);
                if (liveGrant is not null && liveGrant.Contribute(amount, StakeProduct.Casino, 0) > 0)
                {
                    await transaction.SaveGrantAsync(liveGrant, isNew: false);
                }
            }

            List<LedgerEntry> entries = [new(house, Bucket.Available, amount)];
            if (cash != 0)
            {
                entries.Add(new(accountId, Bucket.Available, -cash));
            }

            if (fromBonus != 0)
            {
                entries.Add(new(accountId, Bucket.Bonus, -fromBonus));
            }

            return PostingResult.Post(new Posting(Guid.CreateVersion7(), PostingKind.Debit, idempotencyKey, reference, entries), [account]);
        });

    private Task<WalletOutcome> ClawBackAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        runner.RunAsync(idempotencyKey, PostingKind.Debit, accountId, amount, async transaction =>
        {
            if (await transaction.LockAccountAsync(accountId) is not { } account)
            {
                return PostingResult.Refuse(WalletFailure.AccountNotFound);
            }

            if (WellKnownAccounts.HouseFor(currency) is not { } house)
            {
                return PostingResult.Refuse(WalletFailure.CurrencyMismatch);
            }

            if (account.Pay(amount, currency) is { } failure)
            {
                return PostingResult.Refuse(failure);
            }

            // A clawback undoes winnings, so the loss headroom they earned goes too.
            if (account.Kind == AccountKind.Punter)
            {
                await transaction.AddSpendAsync(accountId, time.GetUtcNow(), 0, -amount, 0);
            }

            return PostingResult.Post(new Posting(Guid.CreateVersion7(), PostingKind.Debit, idempotencyKey, reference,
                [new(accountId, Bucket.Available, -amount), new(house, Bucket.Available, amount)]), [account]);
        });

    private Task<WalletOutcome> MoveAsync(PostingKind kind, string idempotencyKey, Guid accountId, long amount, string currency, string reference, bool openIfMissing = false) =>
        runner.RunAsync(idempotencyKey, kind, accountId, amount, async transaction =>
        {
            var source = kind == PostingKind.Credit ? WellKnownAccounts.HouseFor(currency) : WellKnownAccounts.FundingFor(currency);
            if (source is null)
            {
                return PostingResult.Refuse(WalletFailure.CurrencyMismatch);
            }

            var account = await transaction.LockAccountAsync(accountId)
                ?? (openIfMissing ? await transaction.OpenPunterAccountAsync(accountId, currency) : null);
            if (account is null)
            {
                return PostingResult.Refuse(WalletFailure.AccountNotFound);
            }

            var split = kind == PostingKind.Credit && account.Kind == AccountKind.Punter ? await SplitCreditAsync(transaction, idempotencyKey, account, amount, reference) : null;
            var cash = split?.Cash ?? amount;
            if (account.ReceiveSplit(amount, currency, cash, split?.Bonus ?? 0) is { } failure)
            {
                return PostingResult.Refuse(failure);
            }

            if (account.Kind == AccountKind.Punter && await RecordSpendAsync(transaction, kind, account, cash) is { } refused)
            {
                return refused;
            }

            List<LedgerEntry> entries = [new(source.Value, Bucket.Available, -amount)];
            if (cash != 0 || split is null)
            {
                entries.Add(new(accountId, Bucket.Available, cash));
            }

            if (split is { Bonus: > 0 })
            {
                entries.Add(new(accountId, Bucket.Bonus, split.Bonus));
            }

            if (split is { ToPromotions: > 0 })
            {
                entries.Add(new(split.Promotions, Bucket.Available, split.ToPromotions));
            }

            return PostingResult.Post(new Posting(Guid.CreateVersion7(), kind, idempotencyKey, reference, entries), [account]);
        });

    private sealed record CreditSplit(long Cash, long Bonus, long ToPromotions, Guid Promotions);

    /// <summary>Splits a credit the way its stake was paid; null when no offer paid for it, so it is all cash.</summary>
    private static async Task<CreditSplit?> SplitCreditAsync(IWalletTransaction transaction, string idempotencyKey, Account account, long amount, string reference)
    {
        var stake = await FindPaidStakeAsync(transaction, idempotencyKey, account.AccountId, reference);
        if (stake is null || (stake.BonusAmount == 0 && stake.FreeBetId is null) || WellKnownAccounts.PromotionsFor(account.Currency) is not { } promotions)
        {
            return null;
        }

        var (retain, bonus, cash) = stake.Split(amount);
        if (retain > 0)
        {
            await transaction.SaveStakeAsync(stake with { FreeBetRetained = stake.FreeBetRetained + retain }, isNew: false);
        }

        var grant = bonus > 0 && stake.GrantId is { } grantId ? await transaction.GetGrantAsync(grantId) : null;
        return grant?.State switch
        {
            BonusState.Active => new CreditSplit(cash, bonus, retain, promotions),
            BonusState.Converted => new CreditSplit(cash + bonus, 0, retain, promotions),
            _ => new CreditSplit(cash, 0, retain + bonus, promotions),
        };
    }

    /// <summary>Casino credits follow the latest casino stake; a sports settlement's reference names its coupon ("coupon X settlement").</summary>
    private static Task<BonusStake?> FindPaidStakeAsync(IWalletTransaction transaction, string idempotencyKey, Guid accountId, string reference)
    {
        if (idempotencyKey.StartsWith("casino:", StringComparison.Ordinal))
        {
            return transaction.LatestCasinoStakeAsync(accountId);
        }

        return reference.StartsWith("coupon ", StringComparison.Ordinal) && reference.IndexOf(" settlement", StringComparison.Ordinal) is > 0 and var end
            ? transaction.FindStakeAsync(accountId, reference[..end])
            : Task.FromResult<BonusStake?>(null);
    }

    /// <summary>Deposits and top-ups are judged against deposit limits; a credit is winnings and earns loss headroom back.</summary>
    private async Task<PostingResult?> RecordSpendAsync(IWalletTransaction transaction, PostingKind kind, Account account, long amount)
    {
        var now = time.GetUtcNow();
        if (kind is not (PostingKind.TopUp or PostingKind.Deposit))
        {
            await transaction.AddSpendAsync(account.AccountId, now, 0, amount, 0);
            return null;
        }

        if (SpendPolicy.CheckDeposit(rules.For(account.UserId).In(account.Currency), await transaction.GetSpendAsync(account.AccountId, now), amount, now) is { } refusal)
        {
            return PostingResult.Refuse(refusal, LimitHit.From(refusal, account, "deposit", amount));
        }

        await transaction.AddSpendAsync(account.AccountId, now, 0, 0, amount);
        return null;
    }
}
