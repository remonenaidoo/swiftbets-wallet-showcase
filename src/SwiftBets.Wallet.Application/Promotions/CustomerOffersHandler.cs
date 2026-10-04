using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Application.Promotions;

/// <summary>The offers a customer can see and opt in to, and their switch to turn offers off.</summary>
public sealed class CustomerOffersHandler(IPromotionStore store, OfferEligibility eligibility, TimeProvider time)
{
    public async Task<CustomerOffers> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var optedOut = await store.MarketingOptedOutAsync(userId, cancellationToken);
        var restricted = eligibility.IsRestricted(userId);
        if (optedOut || restricted)
        {
            return new CustomerOffers(optedOut, restricted, []);
        }

        var now = time.GetUtcNow();
        var optedIn = await store.OptedInAsync(userId, cancellationToken);
        var awarded = await store.AwardedAsync(userId, cancellationToken);
        var offers = (await store.ListAsync(cancellationToken)).Select(r => r.Promotion).Where(p => p.IsRunning(now))
            .Select(p => new CustomerOffer(p, optedIn.Contains(p.PromotionId), awarded.Contains(p.PromotionId)));
        return new CustomerOffers(false, false, [.. offers]);
    }

    /// <summary>Opts the customer in; the reason it was refused otherwise: not_found, opted_out or restricted.</summary>
    public async Task<string?> OptInAsync(Guid userId, Guid promotionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (await store.GetAsync(promotionId, cancellationToken) is not { } record || !record.Promotion.IsRunning(now))
        {
            return "not_found";
        }

        if (eligibility.IsRestricted(userId))
        {
            return "restricted";
        }

        if (await store.MarketingOptedOutAsync(userId, cancellationToken))
        {
            return "opted_out";
        }

        await store.OptInAsync(promotionId, userId, now, cancellationToken);
        return null;
    }

    public Task OptOutAsync(Guid userId, Guid promotionId, CancellationToken cancellationToken) => store.OptOutAsync(promotionId, userId, cancellationToken);

    public Task SetMarketingOptedOutAsync(Guid userId, bool optedOut, CancellationToken cancellationToken) =>
        store.SetMarketingOptedOutAsync(userId, optedOut, time.GetUtcNow(), cancellationToken);
}

public sealed record CustomerOffer(Promotion Promotion, bool OptedIn, bool Awarded);

public sealed record CustomerOffers(bool MarketingOptedOut, bool Restricted, IReadOnlyList<CustomerOffer> Offers);
