using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.ResponsibleGambling;
using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Application.Vouchers;

/// <summary>A voucher with its code (null when the caller may not see it), or why the request was refused.</summary>
public sealed record VoucherOutcome(Voucher? Voucher, string? Code, string? Error, string? Message, DateTimeOffset? RetryAfter = null)
{
    public static VoucherOutcome Ok(Voucher voucher, string? code = null) => new(voucher, code, null, null);

    public static VoucherOutcome Fail(string error, string message, DateTimeOffset? retryAfter = null) => new(null, null, error, message, retryAfter);
}

/// <summary>A batch staff issued and the codes in it.</summary>
public sealed record IssuedBatch(VoucherBatch Batch, IReadOnlyList<(Voucher Voucher, string Code)> Vouchers);

/// <summary>
/// Cash vouchers. Buying moves money from the buyer's balance into the voucher liability account; staff batches move it
/// from funding (cash taken at a retailer) or the house (a promotion). Redeeming moves it into the redeemer's account and
/// voiding back where it came from. Each is one posting keyed per voucher, written with the voucher row locked, so a
/// voucher is spent once however many requests race. Failed redeems are counted per customer and per address and lock out.
/// </summary>
public sealed class VoucherHandler(
    PostingRunner runner, IWalletStore wallet, IVoucherStore vouchers, AccountsHandler accounts, IGamblingRules rules, VoucherCodes codes,
    VoucherSettings settings, IVoucherGiftNotifier gifts, TimeProvider time)
{
    public async Task<VoucherOutcome> BuyAsync(Guid userId, string requestKey, long amount, string currency, string? recipientEmail, CancellationToken cancellationToken)
    {
        if (CheckAmount(amount) is { } invalid)
        {
            return invalid;
        }

        if (recipientEmail is { Length: > 0 } && !IsEmail(recipientEmail))
        {
            return VoucherOutcome.Fail("invalid_email", "Enter a valid email address for the recipient, or leave it empty.");
        }

        if ((await wallet.ListAccountsAsync(userId, cancellationToken)).FirstOrDefault(a => a.Currency == currency) is not { } account)
        {
            return VoucherOutcome.Fail("account_not_found", "You have no account in that currency.");
        }

        var issueKey = $"voucher-buy:{userId}:{requestKey}";
        var code = codes.For(issueKey);
        var outcome = await runner.RunAsync(issueKey, PostingKind.VoucherPurchase, account.AccountId, amount, async transaction =>
        {
            var locked = await transaction.LockAccountAsync(account.AccountId);
            if (locked is null || WellKnownAccounts.VouchersFor(currency) is not { } liability)
            {
                return (WalletFailure.AccountNotFound, null, [], null, false);
            }

            if (locked.IsBlacklisted)
            {
                return (WalletFailure.AccountBlacklisted, null, [], null, false);
            }

            var now = time.GetUtcNow();
            if (SpendPolicy.CheckVoucherPurchase(rules.For(locked.UserId).In(currency), now) is { } blocked)
            {
                throw new WalletRefusedException(blocked.Failure, blocked.Detail);
            }

            if (locked.Pay(amount, currency) is { } failure)
            {
                return (failure, null, [], null, false);
            }

            var voucher = new Voucher(Guid.CreateVersion7(), issueKey, code[^4..], amount, currency, VoucherSource.Purchase, locked.AccountId, userId, null,
                string.IsNullOrWhiteSpace(recipientEmail) ? null : recipientEmail.Trim(), VoucherStatus.Active, now, now + Voucher.Lifetime);
            await transaction.InsertVoucherAsync(voucher, VoucherCode.Hash(code));
            return (null, new Posting(Guid.CreateVersion7(), PostingKind.VoucherPurchase, issueKey, $"voucher {voucher.VoucherId}",
                [new(locked.AccountId, Bucket.Available, -amount), new(liability, Bucket.Available, amount)]), [locked], null, false);
        });

        if (outcome.Failure is { } refused)
        {
            return Refused(refused, outcome.Detail);
        }

        var bought = (await vouchers.FindByIssueKeyAsync(issueKey, cancellationToken))!;
        if (outcome.WasApplied && bought.RecipientEmail is not null)
        {
            await gifts.SendAsync(bought, code);
        }

        return VoucherOutcome.Ok(bought, code);
    }

    public async Task<VoucherOutcome> RedeemAsync(Guid userId, string? input, string addressSubject, CancellationToken cancellationToken)
    {
        var userSubject = $"user:{userId}";
        var now = time.GetUtcNow();
        if (await vouchers.LockedUntilAsync([userSubject, addressSubject], now, cancellationToken) is { } until)
        {
            return VoucherOutcome.Fail("too_many_attempts", "Too many wrong codes. Try again later.", until);
        }

        var code = VoucherCode.Normalise(input);
        var voucher = code is null ? null : await vouchers.FindByHashAsync(VoucherCode.Hash(code), cancellationToken);
        if (voucher is null)
        {
            await vouchers.RecordFailureAsync(userSubject, settings.UserFailures, now, settings.FailureWindow, settings.LockoutPeriod, cancellationToken);
            await vouchers.RecordFailureAsync(addressSubject, settings.IpFailures, now, settings.FailureWindow, settings.LockoutPeriod, cancellationToken);
            return VoucherOutcome.Fail("invalid_code", "That code is not valid. Check it and try again.");
        }

        if (voucher.Status == VoucherStatus.Redeemed)
        {
            return voucher.RedeemedBy == userId ? VoucherOutcome.Ok(voucher) : AlreadyUsed();
        }

        if (Unusable(voucher, now) is { } unusable)
        {
            return unusable;
        }

        var (account, openFailure) = await accounts.OpenAsync(userId, voucher.Currency, cancellationToken);
        if (account is null)
        {
            return Refused(openFailure ?? WalletFailure.AccountNotFound, null);
        }

        var key = $"voucher:{voucher.VoucherId}:redeem";
        var outcome = await runner.RunAsync(key, PostingKind.VoucherRedeem, account.AccountId, voucher.Amount, async transaction =>
        {
            var current = await transaction.LockVoucherAsync(voucher.VoucherId);
            var at = time.GetUtcNow();
            if (current is null || current.Status != VoucherStatus.Active || current.IsExpired(at))
            {
                return (WalletFailure.InvalidState, null, [], null, false);
            }

            var locked = await transaction.LockAccountAsync(account.AccountId);
            if (locked is null)
            {
                return (WalletFailure.AccountNotFound, null, [], null, false);
            }

            if (locked.Receive(current.Amount, current.Currency) is { } failure)
            {
                return (failure, null, [], null, false);
            }

            // Redeeming puts money in to bet with, so it is judged and counted like a deposit.
            if (SpendPolicy.CheckDeposit(rules.For(locked.UserId).In(locked.Currency), await transaction.GetSpendAsync(locked.AccountId, at), current.Amount, at) is { } refusal)
            {
                throw new WalletRefusedException(refusal.Failure, refusal.Detail,
                    refusal.Limit is { } hit ? new LimitHit(locked.UserId, hit, "deposit", current.Amount, locked.Currency) : null);
            }

            await transaction.AddSpendAsync(locked.AccountId, at, 0, 0, current.Amount);
            await transaction.UpdateVoucherAsync(current with { Status = VoucherStatus.Redeemed, RedeemedBy = userId, RedeemedAt = at });
            return (null, new Posting(Guid.CreateVersion7(), PostingKind.VoucherRedeem, key, $"voucher {current.VoucherId}",
                [new(WellKnownAccounts.VouchersFor(current.Currency)!.Value, Bucket.Available, -current.Amount), new(locked.AccountId, Bucket.Available, current.Amount)]), [locked], null, false);
        });

        if (outcome.Failure is { } refused)
        {
            // Another customer's key, or the voucher changed under the lock: either way it is no longer there to take.
            return refused is WalletFailure.IdempotencyConflict or WalletFailure.InvalidState
                ? Unusable((await vouchers.GetAsync(voucher.VoucherId, cancellationToken))!, time.GetUtcNow()) ?? AlreadyUsed()
                : Refused(refused, outcome.Detail);
        }

        await vouchers.ClearFailuresAsync(userSubject, cancellationToken);
        return VoucherOutcome.Ok((await vouchers.GetAsync(voucher.VoucherId, cancellationToken))!);
    }

    public async Task<(IssuedBatch? Batch, VoucherOutcome? Refusal)> IssueBatchAsync(
        string operatorId, string requestKey, int count, long amount, string currency, VoucherSource source, string note, CancellationToken cancellationToken)
    {
        if (count < 1 || count > settings.MaxBatch)
        {
            return (null, VoucherOutcome.Fail("invalid_count", $"Issue between 1 and {settings.MaxBatch} vouchers at a time."));
        }

        if (CheckAmount(amount) is { } invalid)
        {
            return (null, invalid);
        }

        var from = source switch
        {
            VoucherSource.Retail => WellKnownAccounts.FundingFor(currency),
            VoucherSource.Promotion => WellKnownAccounts.HouseFor(currency),
            _ => null,
        };
        if (from is null || WellKnownAccounts.VouchersFor(currency) is not { } liability)
        {
            return (null, VoucherOutcome.Fail("invalid_source", "Choose retail or promotion, in a supported currency."));
        }

        var batch = await vouchers.SaveBatchAsync(
            new VoucherBatch(Guid.CreateVersion7(), requestKey, count, amount, currency, source, note.Trim(), operatorId, time.GetUtcNow()), cancellationToken);
        if (batch.Count != count || batch.Amount != amount || batch.Currency != currency || batch.Source != source)
        {
            return (null, VoucherOutcome.Fail("idempotency_conflict", "That request key was already used for a different batch."));
        }

        // Each voucher is its own keyed posting, so a retried request finishes a batch that stopped part way.
        for (var i = 0; i < count; i++)
        {
            var issueKey = IssueKey(batch.BatchId, i);
            var code = codes.For(issueKey);
            var outcome = await runner.RunAsync(issueKey, PostingKind.VoucherIssue, liability, amount, async transaction =>
            {
                var now = time.GetUtcNow();
                var voucher = new Voucher(Guid.CreateVersion7(), issueKey, code[^4..], amount, currency, source, from.Value, null, batch.BatchId, null,
                    VoucherStatus.Active, now, now + Voucher.Lifetime);
                await transaction.InsertVoucherAsync(voucher, VoucherCode.Hash(code));
                return (null, new Posting(Guid.CreateVersion7(), PostingKind.VoucherIssue, issueKey, $"voucher batch {batch.BatchId}",
                    [new(from.Value, Bucket.Available, -amount), new(liability, Bucket.Available, amount)]), [], null, false);
            });
            if (outcome.Failure is { } failure)
            {
                return (null, Refused(failure, outcome.Detail));
            }
        }

        return (await BatchCodesAsync(batch.BatchId, cancellationToken), null);
    }

    /// <summary>The batch with every code, for printing again.</summary>
    public async Task<IssuedBatch?> BatchCodesAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (await vouchers.GetBatchAsync(batchId, cancellationToken) is not { } batch)
        {
            return null;
        }

        var issued = await vouchers.SearchAsync(new VoucherSearch(null, null, null, batchId, null, settings.MaxBatch), cancellationToken);
        return new IssuedBatch(batch, [.. issued.OrderBy(v => v.IssueKey.Length).ThenBy(v => v.IssueKey, StringComparer.Ordinal).Select(v => (v, codes.For(v.IssueKey)))]);
    }

    public async Task<VoucherOutcome> VoidAsync(Guid voucherId, string operatorId, string reason, CancellationToken cancellationToken)
    {
        if (await vouchers.GetAsync(voucherId, cancellationToken) is not { } voucher)
        {
            return VoucherOutcome.Fail("not_found", "No voucher with that id.");
        }

        if (voucher.Status != VoucherStatus.Active)
        {
            return VoucherOutcome.Fail("invalid_state", $"The voucher is already {voucher.StatusAt(time.GetUtcNow())}.");
        }

        var key = $"voucher:{voucherId}:void";
        var outcome = await runner.RunAsync(key, PostingKind.VoucherVoid, voucher.SourceAccountId, voucher.Amount, async transaction =>
        {
            var current = await transaction.LockVoucherAsync(voucherId);
            if (current is null || current.Status != VoucherStatus.Active)
            {
                return (WalletFailure.InvalidState, null, [], null, false);
            }

            // A bought voucher goes back to the buyer's balance; an issued one back to funding or the house.
            IReadOnlyList<Account> changed = [];
            if (current.Source == VoucherSource.Purchase)
            {
                var buyer = await transaction.LockAccountAsync(current.SourceAccountId);
                if (buyer is null || buyer.Receive(current.Amount, current.Currency) is not null)
                {
                    return (buyer is null ? WalletFailure.AccountNotFound : WalletFailure.AccountBlacklisted, null, [], null, false);
                }

                changed = [buyer];
            }

            await transaction.UpdateVoucherAsync(current with { Status = VoucherStatus.Voided, VoidedBy = operatorId, VoidReason = reason, VoidedAt = time.GetUtcNow() });
            return (null, new Posting(Guid.CreateVersion7(), PostingKind.VoucherVoid, key, $"voucher {voucherId} voided",
                [new(WellKnownAccounts.VouchersFor(current.Currency)!.Value, Bucket.Available, -current.Amount), new(current.SourceAccountId, Bucket.Available, current.Amount)]), changed, null, false);
        });

        return outcome.Failure is { } failure ? Refused(failure, outcome.Detail) : VoucherOutcome.Ok((await vouchers.GetAsync(voucherId, cancellationToken))!);
    }

    /// <summary>The code of a voucher the customer bought; nobody else's.</summary>
    public string? CodeFor(Voucher voucher, Guid userId) => voucher.PurchasedBy == userId ? codes.For(voucher.IssueKey) : null;

    public static string IssueKey(Guid batchId, int index) => $"voucher-issue:{batchId}:{index}";

    private VoucherOutcome? CheckAmount(long amount) =>
        amount < settings.MinAmount || amount > settings.MaxAmount
            ? VoucherOutcome.Fail("invalid_amount", $"A voucher is worth between {settings.MinAmount / 100} and {settings.MaxAmount / 100}.")
            : null;

    private static VoucherOutcome? Unusable(Voucher voucher, DateTimeOffset now) => voucher switch
    {
        { Status: VoucherStatus.Voided } => VoucherOutcome.Fail("voucher_void", "This voucher was cancelled and cannot be used."),
        { Status: VoucherStatus.Redeemed } => AlreadyUsed(),
        _ when voucher.IsExpired(now) => VoucherOutcome.Fail("voucher_expired", "This voucher has expired."),
        _ => null,
    };

    private static VoucherOutcome AlreadyUsed() => VoucherOutcome.Fail("voucher_used", "This voucher has already been used.");

    private static VoucherOutcome Refused(WalletFailure failure, string? detail) => failure switch
    {
        WalletFailure.InsufficientFunds => VoucherOutcome.Fail("insufficient_funds", "Your balance is too low for this voucher."),
        WalletFailure.AccountRestricted => VoucherOutcome.Fail("account_restricted", "Your account cannot do this right now."),
        WalletFailure.LimitExceeded => VoucherOutcome.Fail("limit_exceeded", $"This would go over your deposit limit ({detail})."),
        WalletFailure.AccountBlacklisted => VoucherOutcome.Fail("account_blocked", "This account cannot receive money."),
        WalletFailure.IdempotencyConflict => VoucherOutcome.Fail("idempotency_conflict", "That request key was already used for something else."),
        _ => VoucherOutcome.Fail("wallet_refused", detail ?? failure.ToString()),
    };

    private static bool IsEmail(string value)
    {
        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        return trimmed.Length <= 254 && at > 0 && trimmed.LastIndexOf('.') > at + 1 && !trimmed.Any(char.IsWhiteSpace);
    }
}
