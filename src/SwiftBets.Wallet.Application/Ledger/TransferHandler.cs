using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>
/// Credits (winnings, refunds), deposits and operator top-ups into a punter account, and debits (resettlement clawbacks)
/// out of it, against the house or funding account of the account's currency. A casino stake takes cash first, then bonus
/// money where the active offer allows casino play. A credit for a stake an offer paid for is split the way the stake was
/// paid: the bonus share stays bonus money while its offer is live, and a free bet's stake goes back to promotions (D166).
/// </summary>
public sealed class TransferHandler(PostingRunner runner, IGamblingRules rules, TimeProvider time, BonusLedgerHandler? bonuses = null)
{
    /// <summary>The reason the casino gives its stake debits; only these may be paid with bonus money.</summary>
    public const string CasinoBetReason = "casino-bet";

    public Task<WalletOutcome> CreditAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        MoveAsync(PostingKind.Credit, idempotencyKey, accountId, amount, currency, reference);

    /// <summary>An operator top-up is also how a first punter account is opened (its id is the user id).</summary>
    public Task<WalletOutcome> TopUpAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        MoveAsync(PostingKind.TopUp, idempotencyKey, accountId, amount, currency, reference, openIfMissing: true);

    /// <summary>A provider-confirmed deposit; the account must already be open (payments opens it first).</summary>
    public Task<WalletOutcome> DepositAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        MoveAsync(PostingKind.Deposit, idempotencyKey, accountId, amount, currency, reference);

    public async Task<WalletOutcome> DebitAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference, string? reason = null)
    {
        if (reason != CasinoBetReason)
        {
            return await ClawBackAsync(idempotencyKey, accountId, amount, currency, reference);
        }

        var outcome = await CasinoStakeAsync(idempotencyKey, accountId, amount, currency, reference);
        if (outcome.WasApplied && bonuses is not null)
        {
            await bonuses.ConvertIfMetAsync(accountId);
        }

        return outcome;
    }

    private Task<WalletOutcome> CasinoStakeAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        runner.RunAsync(idempotencyKey, PostingKind.Debit, accountId, amount, async transaction =>
        {
            var account = await transaction.LockAccountAsync(accountId);
            if (account is null)
            {
                return (WalletFailure.AccountNotFound, null, [], null, false);
            }

            if (WellKnownAccounts.HouseFor(currency) is not { } house)
            {
                return (WalletFailure.CurrencyMismatch, null, [], null, false);
            }

            var now = time.GetUtcNow();
            var grant = account.Kind == AccountKind.Punter ? await transaction.ActiveGrantAsync(accountId) : null;
            var live = grant is not null && grant.IsLive(now);
            var (failure, fromBonus) = account.PayCashThenBonus(amount, currency, live && grant!.Allows(StakeProduct.Casino, 0));
            if (failure is not null)
            {
                return (failure, null, [], null, false);
            }

            if (account.Kind == AccountKind.Punter)
            {
                await transaction.AddSpendAsync(accountId, now, 0, -(amount - fromBonus), 0);
                await transaction.SaveStakeAsync(new BonusStake(idempotencyKey, accountId, reference, StakeProduct.Casino, 0, amount - fromBonus, fromBonus, live ? grant!.GrantId : null, null, 0, 0), isNew: true);
                if (live && grant!.Contribute(amount, StakeProduct.Casino, 0) > 0)
                {
                    await transaction.SaveGrantAsync(grant, isNew: false);
                }
            }

            List<LedgerEntry> entries = [new(house, Bucket.Available, amount)];
            if (amount - fromBonus != 0)
            {
                entries.Add(new(accountId, Bucket.Available, -(amount - fromBonus)));
            }

            if (fromBonus != 0)
            {
                entries.Add(new(accountId, Bucket.Bonus, -fromBonus));
            }

            return (null, new Posting(Guid.CreateVersion7(), PostingKind.Debit, idempotencyKey, reference, entries), [account], null, false);
        });

    private Task<WalletOutcome> ClawBackAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        runner.RunAsync(idempotencyKey, PostingKind.Debit, accountId, amount, async transaction =>
        {
            var account = await transaction.LockAccountAsync(accountId);
            if (account is null)
            {
                return (WalletFailure.AccountNotFound, null, [], null, false);
            }

            if (WellKnownAccounts.HouseFor(currency) is not { } house)
            {
                return (WalletFailure.CurrencyMismatch, null, [], null, false);
            }

            if (account.Pay(amount, currency) is { } failure)
            {
                return (failure, null, [], null, false);
            }

            // A clawback undoes winnings, so the loss headroom they earned goes too.
            if (account.Kind == AccountKind.Punter)
            {
                await transaction.AddSpendAsync(accountId, time.GetUtcNow(), 0, -amount, 0);
            }

            return (null, new Posting(Guid.CreateVersion7(), PostingKind.Debit, idempotencyKey, reference,
                [new(accountId, Bucket.Available, -amount), new(house, Bucket.Available, amount)]), [account], null, false);
        });

    private Task<WalletOutcome> MoveAsync(PostingKind kind, string idempotencyKey, Guid accountId, long amount, string currency, string reference, bool openIfMissing = false) =>
        runner.RunAsync(idempotencyKey, kind, accountId, amount, async transaction =>
        {
            var source = kind == PostingKind.Credit ? WellKnownAccounts.HouseFor(currency) : WellKnownAccounts.FundingFor(currency);
            if (source is null)
            {
                return (WalletFailure.CurrencyMismatch, null, [], null, false);
            }

            var account = await transaction.LockAccountAsync(accountId)
                ?? (openIfMissing ? await transaction.OpenPunterAccountAsync(accountId, currency) : null);
            if (account is null)
            {
                return (WalletFailure.AccountNotFound, null, [], null, false);
            }

            var split = kind == PostingKind.Credit && account.Kind == AccountKind.Punter ? await SplitCreditAsync(transaction, idempotencyKey, account, amount, reference) : null;
            var cash = split?.Cash ?? amount;
            if (account.ReceiveSplit(amount, currency, cash, split?.Bonus ?? 0) is { } failure)
            {
                return (failure, null, [], null, false);
            }

            if (account.Kind == AccountKind.Punter)
            {
                await RecordSpendAsync(transaction, kind, account, cash);
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

            return (null, new Posting(Guid.CreateVersion7(), kind, idempotencyKey, reference, entries), [account], null, false);
        });

    private sealed record CreditSplit(long Cash, long Bonus, long ToPromotions, Guid Promotions);

    /// <summary>
    /// Finds the stake a credit pays for: a coupon's settlement names its coupon, and a casino credit follows the latest
    /// casino stake. Returns null when no offer was involved, so the whole credit is cash.
    /// </summary>
    private static async Task<CreditSplit?> SplitCreditAsync(IWalletTransaction transaction, string idempotencyKey, Account account, long amount, string reference)
    {
        var stake = idempotencyKey.StartsWith("casino:", StringComparison.Ordinal)
            ? await transaction.LatestCasinoStakeAsync(account.AccountId)
            : reference.StartsWith("coupon ", StringComparison.Ordinal) && reference.IndexOf(" settlement", StringComparison.Ordinal) is > 0 and var end
                ? await transaction.FindStakeAsync(account.AccountId, reference[..end])
                : null;
        if (stake is null || (stake.BonusAmount == 0 && stake.FreeBetId is null) || WellKnownAccounts.PromotionsFor(account.Currency) is not { } promotions)
        {
            return null;
        }

        var (retain, bonus, cash) = stake.Split(amount);
        if (retain > 0)
        {
            await transaction.SaveStakeAsync(stake with { FreeBetRetained = stake.FreeBetRetained + retain }, isNew: false);
        }

        // The bonus share stays bonus while its offer is live, is cash once it converted, and is forfeited once it ended.
        var grant = bonus > 0 && stake.GrantId is { } grantId ? await transaction.GetGrantAsync(grantId) : null;
        return grant?.State switch
        {
            BonusState.Active => new CreditSplit(cash, bonus, retain, promotions),
            BonusState.Converted => new CreditSplit(cash + bonus, 0, retain, promotions),
            _ => new CreditSplit(cash, 0, retain + bonus, promotions),
        };
    }

    /// <summary>A deposit or top-up is judged against deposit limits and blocks; a credit is winnings or a refund.</summary>
    private async Task RecordSpendAsync(IWalletTransaction transaction, PostingKind kind, Account account, long amount)
    {
        var now = time.GetUtcNow();
        if (kind is not (PostingKind.TopUp or PostingKind.Deposit))
        {
            await transaction.AddSpendAsync(account.AccountId, now, 0, amount, 0);
            return;
        }

        if (SpendPolicy.CheckDeposit(rules.For(account.UserId).In(account.Currency), await transaction.GetSpendAsync(account.AccountId, now), amount, now) is { } refusal)
        {
            throw new WalletRefusedException(refusal.Failure, refusal.Detail, refusal.Limit is { } hit ? new LimitHit(account.UserId, hit, "deposit", amount, account.Currency) : null);
        }

        await transaction.AddSpendAsync(account.AccountId, now, 0, 0, amount);
    }
}
