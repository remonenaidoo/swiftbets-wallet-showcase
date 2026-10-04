using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>
/// Bonus money's own postings (D166): a grant from the promotions account, conversion to cash once wagering is met, and
/// forfeiture to the promotions account at expiry. Each is keyed by the grant, so a retry or a second worker replays it.
/// A posting's amount is the grant's nominal amount; its entries carry what actually moved.
/// </summary>
public sealed class BonusLedgerHandler(PostingRunner runner, IBonusStore bonuses, TimeProvider time)
{
    public Task<WalletOutcome> GrantAsync(string idempotencyKey, Guid accountId, string currency, BonusGrantTerms terms, string reference) =>
        runner.RunAsync(idempotencyKey, PostingKind.BonusGranted, accountId, terms.Amount, async transaction =>
        {
            var account = await transaction.LockAccountAsync(accountId);
            if (account is null || account.Kind != AccountKind.Punter)
            {
                return (WalletFailure.AccountNotFound, null, [], null, false);
            }

            if (WellKnownAccounts.PromotionsFor(currency) is not { } promotions)
            {
                return (WalletFailure.CurrencyMismatch, null, [], null, false);
            }

            if (account.IsBlacklisted)
            {
                return (WalletFailure.AccountBlacklisted, null, [], null, false);
            }

            if (await transaction.ActiveGrantAsync(accountId) is not null)
            {
                throw new WalletRefusedException(WalletFailure.InvalidState, "a bonus is already active");
            }

            if (account.ReceiveBonus(terms.Amount, currency) is { } failure)
            {
                return (failure, null, [], null, false);
            }

            await transaction.SaveGrantAsync(new BonusGrant(Guid.CreateVersion7(), accountId, terms.PromotionId, terms.Amount, terms.Amount * terms.WageringMultiplier, 0,
                terms.MinOddsHundredths, terms.SportsPercent, terms.CasinoPercent, terms.ExpiresAt, BonusState.Active), isNew: true);
            return (null, new Posting(Guid.CreateVersion7(), PostingKind.BonusGranted, idempotencyKey, reference,
                [new(promotions, Bucket.Available, -terms.Amount), new(accountId, Bucket.Bonus, terms.Amount)]), [account], null, false);
        });

    /// <summary>Turns the bonus balance into cash once the active grant's wagering is met; nothing happens otherwise.</summary>
    public async Task ConvertIfMetAsync(Guid accountId)
    {
        if (await bonuses.ActiveGrantAsync(accountId, CancellationToken.None) is { IsMet: true } grant)
        {
            await CloseAsync(grant, BonusState.Converted);
        }
    }

    /// <summary>The expiry job: converts met grants, forfeits expired ones, returns stray bonus money, expires free bets.</summary>
    public async Task<int> MaintainAsync(int batch, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var done = 0;
        foreach (var grant in await bonuses.GrantsDueAsync(now, batch, cancellationToken))
        {
            done += (await CloseAsync(grant, grant.IsMet && grant.ExpiresAt > now ? BonusState.Converted : BonusState.Expired)).WasApplied ? 1 : 0;
        }

        foreach (var orphan in await bonuses.OrphanBonusAsync(batch, cancellationToken))
        {
            done += (await ReturnOrphanAsync(orphan)).WasApplied ? 1 : 0;
        }

        return done + await bonuses.ExpireFreeBetsAsync(now, cancellationToken);
    }

    private Task<WalletOutcome> CloseAsync(BonusGrant known, BonusState ending)
    {
        var converting = ending == BonusState.Converted;
        var kind = converting ? PostingKind.BonusConverted : PostingKind.BonusForfeited;
        return runner.RunAsync($"bonus:{known.GrantId:N}:{(converting ? "convert" : "expire")}", kind, known.AccountId, known.Amount, async transaction =>
        {
            var account = await transaction.LockAccountAsync(known.AccountId);
            var grant = await transaction.ActiveGrantAsync(known.AccountId);
            var now = time.GetUtcNow();
            if (account is null || grant is null || grant.GrantId != known.GrantId || (converting ? !grant.IsMet || grant.ExpiresAt <= now : grant.ExpiresAt > now)
                || WellKnownAccounts.PromotionsFor(account.Currency) is not { } promotions)
            {
                return (WalletFailure.InvalidState, null, [], null, false);
            }

            grant.Close(ending);
            await transaction.SaveGrantAsync(grant, isNew: false);
            var moved = converting ? account.ConvertBonus() : account.TakeBonus();
            return (null, new Posting(Guid.CreateVersion7(), kind, $"bonus:{known.GrantId:N}:{(converting ? "convert" : "expire")}", $"bonus {known.GrantId}",
                [new(account.AccountId, Bucket.Bonus, -moved), converting ? new(account.AccountId, Bucket.Available, moved) : new(promotions, Bucket.Available, moved)]), [account], null, false);
        });
    }

    private Task<WalletOutcome> ReturnOrphanAsync(OrphanBonus orphan)
    {
        var key = $"bonus:{orphan.AccountId:N}:stray:{Guid.CreateVersion7():N}";
        return runner.RunAsync(key, PostingKind.BonusForfeited, orphan.AccountId, orphan.Bonus, async transaction =>
        {
            var account = await transaction.LockAccountAsync(orphan.AccountId);
            if (account is null || account.Bonus <= 0 || await transaction.ActiveGrantAsync(orphan.AccountId) is not null
                || WellKnownAccounts.PromotionsFor(account.Currency) is not { } promotions)
            {
                return (WalletFailure.InvalidState, null, [], null, false);
            }

            var moved = account.TakeBonus();
            return (null, new Posting(Guid.CreateVersion7(), PostingKind.BonusForfeited, key, "bonus returned after its offer ended",
                [new(account.AccountId, Bucket.Bonus, -moved), new(promotions, Bucket.Available, moved)]), [account], null, false);
        });
    }
}

/// <summary>What a bonus grant gives and asks: wagering is the amount times the multiplier.</summary>
public sealed record BonusGrantTerms(long Amount, int WageringMultiplier, int MinOddsHundredths, byte SportsPercent, byte CasinoPercent, DateTimeOffset ExpiresAt, Guid? PromotionId);
