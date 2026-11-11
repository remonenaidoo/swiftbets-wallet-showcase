using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>Bonus postings (grant, conversion, forfeit), each keyed by its grant so a retry or a second worker replays it.</summary>
public sealed class BonusLedgerHandler(PostingRunner runner, IBonusStore bonuses, TimeProvider time)
{
    public Task<WalletOutcome> GrantAsync(string idempotencyKey, Guid accountId, string currency, BonusGrantTerms terms, string reference) =>
        runner.RunAsync(idempotencyKey, PostingKind.BonusGranted, accountId, terms.Amount, async transaction =>
        {
            var account = await transaction.LockAccountAsync(accountId);
            if (account is null || account.Kind != AccountKind.Punter)
            {
                return PostingResult.Refuse(WalletFailure.AccountNotFound);
            }

            if (WellKnownAccounts.PromotionsFor(currency) is not { } promotions)
            {
                return PostingResult.Refuse(WalletFailure.CurrencyMismatch);
            }

            if (account.IsBlacklisted)
            {
                return PostingResult.Refuse(WalletFailure.AccountBlacklisted);
            }

            if (await transaction.ActiveGrantAsync(accountId) is not null)
            {
                return PostingResult.Refuse(WalletFailure.InvalidState, "a bonus is already active");
            }

            if (account.ReceiveBonus(terms.Amount, currency) is { } failure)
            {
                return PostingResult.Refuse(failure);
            }

            await transaction.SaveGrantAsync(new BonusGrant(Guid.CreateVersion7(), accountId, terms.PromotionId, terms.Amount, terms.Amount * terms.WageringMultiplier, 0,
                terms.MinOddsHundredths, terms.SportsPercent, terms.CasinoPercent, terms.ExpiresAt, BonusState.Active), isNew: true);
            return PostingResult.Post(new Posting(Guid.CreateVersion7(), PostingKind.BonusGranted, idempotencyKey, reference,
                [new(promotions, Bucket.Available, -terms.Amount), new(accountId, Bucket.Bonus, terms.Amount)]), [account]);
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
        var key = $"bonus:{known.GrantId:N}:{(converting ? "convert" : "expire")}";
        return runner.RunAsync(key, kind, known.AccountId, known.Amount, async transaction =>
        {
            var account = await transaction.LockAccountAsync(known.AccountId);
            var grant = await transaction.ActiveGrantAsync(known.AccountId);
            var now = time.GetUtcNow();
            if (account is null || grant is null || grant.GrantId != known.GrantId || (converting ? !grant.IsMet || grant.ExpiresAt <= now : grant.ExpiresAt > now)
                || WellKnownAccounts.PromotionsFor(account.Currency) is not { } promotions)
            {
                return PostingResult.Refuse(WalletFailure.InvalidState);
            }

            grant.Close(ending);
            await transaction.SaveGrantAsync(grant, isNew: false);
            var moved = converting ? account.ConvertBonus() : account.TakeBonus();
            return PostingResult.Post(new Posting(Guid.CreateVersion7(), kind, key, $"bonus {known.GrantId}",
                [new(account.AccountId, Bucket.Bonus, -moved), converting ? new(account.AccountId, Bucket.Available, moved) : new(promotions, Bucket.Available, moved)]), [account]);
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
                return PostingResult.Refuse(WalletFailure.InvalidState);
            }

            var moved = account.TakeBonus();
            return PostingResult.Post(new Posting(Guid.CreateVersion7(), PostingKind.BonusForfeited, key, "bonus returned after its offer ended",
                [new(account.AccountId, Bucket.Bonus, -moved), new(promotions, Bucket.Available, moved)]), [account]);
        });
    }
}

/// <summary>What a bonus grant gives and asks: wagering is the amount times the multiplier.</summary>
public sealed record BonusGrantTerms(long Amount, int WageringMultiplier, int MinOddsHundredths, byte SportsPercent, byte CasinoPercent, DateTimeOffset ExpiresAt, Guid? PromotionId);
