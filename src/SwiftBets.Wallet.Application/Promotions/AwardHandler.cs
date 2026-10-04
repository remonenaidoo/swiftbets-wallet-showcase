using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Promotions;

/// <summary>
/// Turns domain events into awards (D166): a successful deposit earns running deposit matches, and the settlement of a
/// customer's first bet earns running first-bet free bets. Each customer gets an offer at most once; the bonus posting is
/// keyed by offer and customer and the award row is unique, so a redelivered event awards nothing more. Every decision,
/// including a refusal, is recorded for the console.
/// </summary>
public sealed class AwardHandler(IPromotionStore store, OfferEligibility eligibility, AccountsHandler accounts, BonusLedgerHandler ledger)
{
    public async Task OnDepositAsync(Guid paymentId, Guid userId, long amount, string currency, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var trigger = $"deposit:{paymentId:N}";
        foreach (var promotion in await CandidatesAsync(PromotionKind.DepositMatch, userId, currency, at, cancellationToken))
        {
            if (await RefusalAsync(promotion, userId, trigger, at, cancellationToken))
            {
                continue;
            }

            var award = promotion.DepositAward(amount);
            if (award == 0)
            {
                await RecordAsync(promotion, userId, trigger, AwardOutcome.Refused, "below the minimum deposit", 0, at, cancellationToken);
                continue;
            }

            var account = (await accounts.ListAsync(userId, cancellationToken)).FirstOrDefault(a => a.Currency == currency);
            var outcome = account is null
                ? WalletOutcome.Failed(Domain.WalletFailure.AccountNotFound)
                : await ledger.GrantAsync($"promo:{promotion.PromotionId:N}:{userId:N}", account.AccountId, currency,
                    new BonusGrantTerms(award, promotion.WageringMultiplier, promotion.MinOddsHundredths, promotion.SportsPercent, promotion.CasinoPercent,
                        at.AddDays(promotion.ValidDays), promotion.PromotionId), $"offer {promotion.Name}");
            await RecordAsync(promotion, userId, trigger, outcome.Failure is null ? AwardOutcome.Awarded : AwardOutcome.Refused,
                outcome.Failure is null ? null : outcome.Detail ?? outcome.Failure.ToString(), outcome.Failure is null ? award : 0, at, cancellationToken);
        }
    }

    public async Task OnCouponSettledAsync(Guid couponId, Guid userId, int settlementVersion, string currency, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (settlementVersion != 1)
        {
            return;
        }

        var trigger = $"coupon:{couponId:N}";
        foreach (var promotion in await CandidatesAsync(PromotionKind.FirstBetFreeBet, userId, currency, at, cancellationToken))
        {
            if (!await store.IsFirstStakeAsync(userId, $"coupon {couponId}", cancellationToken) || await RefusalAsync(promotion, userId, trigger, at, cancellationToken))
            {
                continue;
            }

            var account = (await accounts.ListAsync(userId, cancellationToken)).FirstOrDefault(a => a.Currency == currency);
            if (account is null)
            {
                continue;
            }

            await store.AwardFreeBetAsync(
                new PromotionAward(Guid.CreateVersion7(), promotion.PromotionId, userId, trigger, AwardOutcome.Awarded, null, promotion.MaxAward, at),
                new FreeBet(Guid.CreateVersion7(), account.AccountId, promotion.PromotionId, promotion.MaxAward, promotion.MinOddsHundredths, at.AddDays(promotion.ValidDays), FreeBetState.Available),
                cancellationToken);
        }
    }

    /// <summary>Running offers the customer has not had yet and has opted in to where opting in is required.</summary>
    private async Task<IEnumerable<Promotion>> CandidatesAsync(PromotionKind kind, Guid userId, string currency, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var running = await store.RunningAsync(kind, currency, at, cancellationToken);
        if (running.Count == 0)
        {
            return [];
        }

        var awarded = await store.AwardedAsync(userId, cancellationToken);
        var optedIn = await store.OptedInAsync(userId, cancellationToken);
        return running.Where(p => !awarded.Contains(p.PromotionId) && (!p.OptInRequired || optedIn.Contains(p.PromotionId)));
    }

    /// <summary>Records a refusal when responsible gambling or the customer's choice rules the offer out.</summary>
    private async Task<bool> RefusalAsync(Promotion promotion, Guid userId, string trigger, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (await eligibility.RefusalAsync(userId, cancellationToken) is not { } reason)
        {
            return false;
        }

        await RecordAsync(promotion, userId, trigger, AwardOutcome.Refused, reason, 0, at, cancellationToken);
        return true;
    }

    private Task<bool> RecordAsync(Promotion promotion, Guid userId, string trigger, AwardOutcome outcome, string? reason, long amount, DateTimeOffset at, CancellationToken cancellationToken) =>
        store.RecordAwardAsync(new PromotionAward(Guid.CreateVersion7(), promotion.PromotionId, userId, trigger, outcome, reason, amount, at), cancellationToken);
}
