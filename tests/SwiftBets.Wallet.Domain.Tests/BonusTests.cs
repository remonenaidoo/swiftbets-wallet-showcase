using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Domain.Tests;

public sealed class BonusTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_free_bet_stake_keeps_its_stake_back_from_the_first_credit()
    {
        var stake = new BonusStake("k", Guid.NewGuid(), "coupon 1", StakeProduct.Sport, 300, 0, 0, null, Guid.NewGuid(), 1_000, 0);

        stake.Split(3_000).ShouldBe((1_000L, 0L, 2_000L));
    }

    [Fact]
    public void An_offer_with_its_end_before_its_start_is_refused()
    {
        var offer = new Promotion(Guid.NewGuid(), "Welcome", PromotionKind.DepositMatch, "ZAR", 100, 50_000, 0, 5, 150, 100, 0, 7, true, Now, Now.AddDays(-1), "Terms.", true);

        offer.Problem().ShouldBe("The offer must end after it starts.");
        (offer with { EndsAt = Now.AddDays(1) }).Problem().ShouldBeNull();
    }
}
