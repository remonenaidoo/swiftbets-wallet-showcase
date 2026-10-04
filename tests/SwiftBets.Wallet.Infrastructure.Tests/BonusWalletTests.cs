using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Application.Promotions;
using SwiftBets.Wallet.Application.Reconciliation;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;
using SwiftBets.Wallet.Infrastructure.Persistence;

namespace SwiftBets.Wallet.Infrastructure.Tests;

/// <summary>The bonus wallet and promotions (D166): cash first, wagering, free bets, forfeits, expiry and awards.</summary>
public sealed class BonusWalletTests(SqlServerFixture sql)
{
    private static readonly Guid Punter = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Promotions = Guid.Parse("00000000-0000-0000-0000-00000000c001");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_stake_takes_cash_first_then_bonus_at_eligible_odds()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);

        var reserve = await w.Reserve.HandleAsync("c1_reserve", Punter, 102_000, "ZAR", "coupon c1", new StakeTerms(StakeProduct.Sport, 200));

        reserve.Reservation!.BonusAmount.ShouldBe(2_000);
        reserve.Account!.Available.ShouldBe(0);
        reserve.Account.Bonus.ShouldBe(3_000);
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task Bonus_money_is_refused_below_the_offer_minimum_odds()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);

        var reserve = await w.Reserve.HandleAsync("c1_reserve", Punter, 102_000, "ZAR", "coupon c1", new StakeTerms(StakeProduct.Sport, 120));

        reserve.Failure.ShouldBe(WalletFailure.InsufficientFunds);
        (await w.Store.GetAccountAsync(Punter, CancellationToken.None))!.Bonus.ShouldBe(5_000);
    }

    [Fact]
    public async Task Winnings_on_a_bonus_stake_stay_bonus_until_wagering_is_met()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);
        var couponId = Guid.NewGuid();
        var reserve = await w.Reserve.HandleAsync("c_reserve", Punter, 102_000, "ZAR", $"coupon {couponId}", new StakeTerms(StakeProduct.Sport, 200));
        await w.Settle.CaptureAsync("c_capture", reserve.Reservation!.ReservationId, CancellationToken.None);

        var credit = await w.Transfer.CreditAsync("c_pay", Punter, 204_000, "ZAR", $"coupon {couponId} settlement v1");

        credit.Account!.Bonus.ShouldBe(3_000 + 4_000);
        credit.Account.Available.ShouldBe(200_000);
        (await w.Bonuses.ActiveGrantAsync(Punter, CancellationToken.None))!.Wagered.ShouldBe(102_000);
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task Met_wagering_converts_the_bonus_balance_to_cash()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 1);
        var reserve = await w.Reserve.HandleAsync("c_reserve", Punter, 6_000, "ZAR", "coupon c", new StakeTerms(StakeProduct.Sport, 200));

        await w.Settle.CaptureAsync("c_capture", reserve.Reservation!.ReservationId, CancellationToken.None);

        var account = (await w.Store.GetAccountAsync(Punter, CancellationToken.None))!;
        account.Bonus.ShouldBe(0);
        account.Available.ShouldBe(100_000 - 6_000 + 5_000);
        (await w.Bonuses.ActiveGrantAsync(Punter, CancellationToken.None)).ShouldBeNull();
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task A_winning_free_bet_pays_the_return_without_the_stake()
    {
        var w = await WalletAsync();
        var freeBet = await FreeBetAsync(w, 1_000, minOdds: 200);
        var couponId = Guid.NewGuid();
        var reserve = await w.Reserve.HandleAsync("fb_reserve", Punter, 1_000, "ZAR", $"coupon {couponId}", new StakeTerms(StakeProduct.Sport, 300, freeBet));
        await w.Settle.CaptureAsync("fb_capture", reserve.Reservation!.ReservationId, CancellationToken.None);

        var credit = await w.Transfer.CreditAsync("fb_pay", Punter, 3_000, "ZAR", $"coupon {couponId} settlement v1");

        credit.Account!.Available.ShouldBe(100_000 + 2_000);
        await using var connection = new SqlConnection(w.ConnectionString);
        (await connection.ExecuteScalarAsync<long>($"SELECT SUM(Amount) FROM wallet.LedgerEntries WHERE AccountId = '{Promotions}'")).ShouldBe(0);
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task A_free_bet_below_its_minimum_odds_is_refused_and_stays_usable()
    {
        var w = await WalletAsync();
        var freeBet = await FreeBetAsync(w, 1_000, minOdds: 200);

        var reserve = await w.Reserve.HandleAsync("fb_reserve", Punter, 1_000, "ZAR", "coupon x", new StakeTerms(StakeProduct.Sport, 150, freeBet));

        reserve.Failure.ShouldBe(WalletFailure.InvalidState);
        (await w.Bonuses.AvailableFreeBetsAsync(Punter, _time.GetUtcNow(), CancellationToken.None)).Single().FreeBetId.ShouldBe(freeBet);
    }

    [Fact]
    public async Task A_withdrawal_request_forfeits_the_bonus()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);

        var hold = await w.Reserve.HoldWithdrawalAsync("wd_hold", Punter, 1_000, "ZAR", "wd");

        hold.Account!.Bonus.ShouldBe(0);
        (await w.Bonuses.ActiveGrantAsync(Punter, CancellationToken.None)).ShouldBeNull();
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task A_refused_withdrawal_keeps_the_bonus()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);

        (await w.Reserve.HoldWithdrawalAsync("wd_hold", Punter, 999_000, "ZAR", "wd")).Failure.ShouldBe(WalletFailure.InsufficientFunds);

        (await w.Store.GetAccountAsync(Punter, CancellationToken.None))!.Bonus.ShouldBe(5_000);
    }

    [Fact]
    public async Task The_expiry_job_forfeits_an_expired_bonus_and_leaves_a_live_one()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);

        (await w.BonusLedger.MaintainAsync(50, CancellationToken.None)).ShouldBe(0);
        (await w.Store.GetAccountAsync(Punter, CancellationToken.None))!.Bonus.ShouldBe(5_000);

        _time.Advance(TimeSpan.FromDays(8));
        (await w.BonusLedger.MaintainAsync(50, CancellationToken.None)).ShouldBe(1);
        (await w.Store.GetAccountAsync(Punter, CancellationToken.None))!.Bonus.ShouldBe(0);
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task Casino_stakes_use_bonus_after_cash_only_where_the_offer_allows_casino()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40, casinoPercent: 10);

        (await w.Transfer.DebitAsync("casino:p:1", Punter, 101_000, "ZAR", "casino bet 1", TransferHandler.CasinoBetReason)).Account!.Bonus.ShouldBe(4_000);
        var win = await w.Transfer.CreditAsync("casino:p:2", Punter, 2_020, "ZAR", "casino win 2");

        win.Account!.Bonus.ShouldBe(4_020);
        win.Account.Available.ShouldBe(2_000);
        (await w.Bonuses.ActiveGrantAsync(Punter, CancellationToken.None))!.Wagered.ShouldBe(10_100);
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task Casino_stakes_cannot_use_bonus_from_a_sports_only_offer()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40, casinoPercent: 0);

        (await w.Transfer.DebitAsync("casino:p:1", Punter, 101_000, "ZAR", "casino bet 1", TransferHandler.CasinoBetReason)).Failure.ShouldBe(WalletFailure.InsufficientFunds);
    }

    [Fact]
    public async Task Daily_totals_carry_the_offer_money_and_the_ledger_reconciles()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);
        await w.Reserve.HoldWithdrawalAsync("wd_hold", Punter, 1_000, "ZAR", "wd");

        var totals = await w.Reconciliation.DayTotalsAsync(DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime), CancellationToken.None);

        totals.BonusGranted.ShouldBe(5_000);
        totals.BonusReturned.ShouldBe(5_000);
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task Reconciliation_reports_a_bonus_balance_that_drifts_from_its_entries()
    {
        var w = await WalletAsync();
        await GrantAsync(w, 5_000, multiplier: 40);
        await using var connection = new SqlConnection(w.ConnectionString);
        await connection.ExecuteAsync($"UPDATE wallet.Accounts SET Bonus = Bonus + 1 WHERE AccountId = '{Punter}'");

        var report = await new ReconcileLedgerHandler(w.Reconciliation, _time).HandleAsync(CancellationToken.None);

        report.Drifts.ShouldContain(d => d.AccountId == Punter && d.Expected == 5_000 && d.Actual == 5_001);
    }

    [Fact]
    public async Task A_deposit_earns_its_match_once_however_often_the_event_arrives()
    {
        var w = await WalletAsync();
        var promotion = await PromotionAsync(w, PromotionKind.DepositMatch, optIn: false);
        var awards = Awards(w, FixedRules.None);
        var paymentId = Guid.NewGuid();

        await awards.OnDepositAsync(paymentId, Punter, 20_000, "ZAR", _time.GetUtcNow(), CancellationToken.None);
        await awards.OnDepositAsync(paymentId, Punter, 20_000, "ZAR", _time.GetUtcNow(), CancellationToken.None);

        (await w.Store.GetAccountAsync(Punter, CancellationToken.None))!.Bonus.ShouldBe(10_000);
        (await w.Promotions.AwardsAsync(promotion, CancellationToken.None)).ShouldHaveSingleItem().Outcome.ShouldBe(AwardOutcome.Awarded);
        await AssertCleanAsync(w);
    }

    [Fact]
    public async Task A_self_excluded_customer_gets_no_award_and_the_refusal_is_recorded()
    {
        var w = await WalletAsync();
        var promotion = await PromotionAsync(w, PromotionKind.DepositMatch, optIn: false);
        var excluded = new FixedRules(new GamblingRules([], [new SpendBlock(BlockKind.Exclusion, _time.GetUtcNow().AddDays(-1), null)]));

        await Awards(w, excluded).OnDepositAsync(Guid.NewGuid(), Punter, 20_000, "ZAR", _time.GetUtcNow(), CancellationToken.None);

        (await w.Store.GetAccountAsync(Punter, CancellationToken.None))!.Bonus.ShouldBe(0);
        (await w.Promotions.AwardsAsync(promotion, CancellationToken.None)).ShouldHaveSingleItem().Reason.ShouldBe("account restricted");
    }

    [Fact]
    public async Task The_first_bet_settling_earns_an_opted_in_customer_a_free_bet_and_later_bets_do_not()
    {
        var w = await WalletAsync();
        var promotion = await PromotionAsync(w, PromotionKind.FirstBetFreeBet, optIn: true);
        var offers = new CustomerOffersHandler(w.Promotions, new OfferEligibility(FixedRules.None, w.Promotions, _time), _time);
        (await offers.OptInAsync(Punter, promotion, CancellationToken.None)).ShouldBeNull();
        var awards = Awards(w, FixedRules.None);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        foreach (var coupon in new[] { first, second })
        {
            var reserve = await w.Reserve.HandleAsync($"{coupon:N}_reserve", Punter, 1_000, "ZAR", $"coupon {coupon}");
            await w.Settle.CaptureAsync($"{coupon:N}_capture", reserve.Reservation!.ReservationId, CancellationToken.None);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        await awards.OnCouponSettledAsync(second, Punter, 1, "ZAR", _time.GetUtcNow(), CancellationToken.None);
        (await w.Bonuses.AvailableFreeBetsAsync(Punter, _time.GetUtcNow(), CancellationToken.None)).ShouldBeEmpty();
        await awards.OnCouponSettledAsync(first, Punter, 1, "ZAR", _time.GetUtcNow(), CancellationToken.None);

        (await w.Bonuses.AvailableFreeBetsAsync(Punter, _time.GetUtcNow(), CancellationToken.None)).ShouldHaveSingleItem().Amount.ShouldBe(2_500);
    }

    [Fact]
    public async Task A_customer_who_turned_offers_off_sees_none_and_cannot_opt_in()
    {
        var w = await WalletAsync();
        var promotion = await PromotionAsync(w, PromotionKind.FirstBetFreeBet, optIn: true);
        var offers = new CustomerOffersHandler(w.Promotions, new OfferEligibility(FixedRules.None, w.Promotions, _time), _time);

        await offers.SetMarketingOptedOutAsync(Punter, true, CancellationToken.None);

        (await offers.OptInAsync(Punter, promotion, CancellationToken.None)).ShouldBe("opted_out");
        (await offers.ListAsync(Punter, CancellationToken.None)).Offers.ShouldBeEmpty();
    }

    private sealed record Wallet(
        string ConnectionString, SqlWalletStore Store, SqlBonusStore Bonuses, SqlPromotionStore Promotions, SqlReconciliationStore Reconciliation,
        ReserveFundsHandler Reserve, SettleReservationHandler Settle, TransferHandler Transfer, BonusLedgerHandler BonusLedger);

    private async Task<Wallet> WalletAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("bonus_" + Guid.NewGuid().ToString("N")[..10]);
        var result = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true" }]);
        (result is Task<int> task ? await task : (int)result!).ShouldBe(0);
        var connections = new SqlServerConnectionFactory(connectionString);
        var store = new SqlWalletStore(connections, _time);
        var bonuses = new SqlBonusStore(connections);
        var ledger = new BonusLedgerHandler(new PostingRunner(store), bonuses, _time);
        return new Wallet(connectionString, store, bonuses, new SqlPromotionStore(connections, _time), new SqlReconciliationStore(connections),
            new ReserveFundsHandler(new PostingRunner(store), FixedRules.None, _time),
            new SettleReservationHandler(new PostingRunner(store), store, ledger),
            new TransferHandler(new PostingRunner(store), FixedRules.None, _time, ledger),
            ledger);
    }

    private AwardHandler Awards(Wallet w, IGamblingRules rules) =>
        new(w.Promotions, new OfferEligibility(rules, w.Promotions, _time), new AccountsHandler(w.Store), w.BonusLedger);

    private async Task GrantAsync(Wallet w, long amount, int multiplier, byte casinoPercent = 0) =>
        (await w.BonusLedger.GrantAsync($"grant_{Guid.NewGuid():N}", Punter, "ZAR",
            new BonusGrantTerms(amount, multiplier, 150, 100, casinoPercent, _time.GetUtcNow().AddDays(7), null), "test bonus")).Failure.ShouldBeNull();

    private async Task<Guid> FreeBetAsync(Wallet w, long amount, int minOdds)
    {
        var promotion = await PromotionAsync(w, PromotionKind.FirstBetFreeBet, optIn: false);
        var freeBet = new FreeBet(Guid.NewGuid(), Punter, promotion, amount, minOdds, _time.GetUtcNow().AddDays(7), FreeBetState.Available);
        (await w.Promotions.AwardFreeBetAsync(new PromotionAward(Guid.NewGuid(), promotion, Punter, "test", AwardOutcome.Awarded, null, amount, _time.GetUtcNow()), freeBet, CancellationToken.None)).ShouldBeTrue();
        return freeBet.FreeBetId;
    }

    private async Task<Guid> PromotionAsync(Wallet w, PromotionKind kind, bool optIn)
    {
        var now = _time.GetUtcNow();
        var draft = new Promotion(Guid.Empty, "Welcome offer", kind, "ZAR", 50, kind == PromotionKind.DepositMatch ? 50_000 : 2_500, 1_000, 10, 150, 100, 0, 7, optIn,
            now.AddDays(-1), now.AddDays(30), "Plain terms.", true);
        var (saved, problem) = await new PromotionAdminHandler(w.Promotions, _time).SaveAsync(null, draft, "admin", CancellationToken.None);
        problem.ShouldBeNull();
        return saved!.Promotion.PromotionId;
    }

    private async Task AssertCleanAsync(Wallet w) =>
        (await new ReconcileLedgerHandler(w.Reconciliation, _time).HandleAsync(CancellationToken.None)).IsClean.ShouldBeTrue();
}
