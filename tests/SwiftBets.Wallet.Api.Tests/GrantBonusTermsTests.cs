using Grpc.Core;
using SwiftBets.Contracts.Grpc.Wallet.V1;
using SwiftBets.Wallet.Api.Grpc;

namespace SwiftBets.Wallet.Api.Tests;

/// <summary>GrantBonus maps its request onto the bonus ledger's terms (D199).</summary>
public sealed class GrantBonusTermsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_referral_bonus_request_becomes_terms_that_expire_after_its_days()
    {
        var terms = WalletGrpcService.BonusTerms(Request(wagering: 1), Now);

        terms.Amount.ShouldBe(5_000);
        terms.WageringMultiplier.ShouldBe(1);
        terms.MinOddsHundredths.ShouldBe(150);
        terms.ExpiresAt.ShouldBe(Now.AddDays(30));
        terms.PromotionId.ShouldBeNull();
    }

    [Fact]
    public void Terms_out_of_range_are_refused_as_invalid_arguments()
    {
        Should.Throw<RpcException>(() => WalletGrpcService.BonusTerms(Request(wagering: 51), Now)).StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    private static GrantBonusRequest Request(int wagering) => new()
    {
        IdempotencyKey = "promo:referral:x",
        AccountId = Guid.NewGuid().ToString(),
        Amount = new Money { MinorUnits = 5_000, Currency = "ZAR" },
        Reference = "referral reward",
        WageringMultiplier = wagering,
        MinOddsHundredths = 150,
        SportsPercent = 100,
        CasinoPercent = 100,
        ValidDays = 30,
    };
}
