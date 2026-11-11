using SwiftBets.Wallet.Application.Ports;
using Microsoft.Extensions.Time.Testing;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Contracts.Compliance;
using SwiftBets.Contracts.Money;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.ResponsibleGambling;
using SwiftBets.Wallet.Infrastructure.Persistence;
using SwiftBets.Wallet.Infrastructure.ResponsibleGambling;

namespace SwiftBets.Wallet.Infrastructure.Tests;

public sealed class ResponsibleGamblingTests(SqlServerFixture sql)
{
    private static readonly Guid Punter = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));

    private static GamblingRules Limit(SpendKind kind, SpendPeriod period, long amount, long? pending = null, DateTimeOffset? due = null) =>
        new([new SpendLimit(kind, period, amount, pending, due)], []);

    [Fact]
    public async Task Twenty_concurrent_reserves_against_a_stake_limit_admit_exactly_the_allowed_amount()
    {
        var (store, connectionString) = await WalletAsync();
        var rules = new FixedRules(Limit(SpendKind.Stake, SpendPeriod.Day, 10_000));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            TestLedger.Reserve(connectionString, rules, time).HandleAsync($"c{i}_reserve", Punter, 1_000, "ZAR", $"c{i}")));

        outcomes.Count(o => o.WasApplied).ShouldBe(10);
        outcomes.Where(o => !o.WasApplied).ShouldAllBe(o => o.Failure == WalletFailure.LimitExceeded && o.Detail == "stake limit per day: 0 left");
        (await store.GetAccountAsync(Punter, CancellationToken.None))!.Reserved.ShouldBe(10_000);
    }

    [Fact]
    public async Task Released_stake_gives_the_headroom_back()
    {
        var (store, connectionString) = await WalletAsync();
        var rules = new FixedRules(Limit(SpendKind.Stake, SpendPeriod.Week, 3_000));
        var reserve = TestLedger.Reserve(connectionString, rules, time);
        var reservation = (await reserve.HandleAsync("a_reserve", Punter, 3_000, "ZAR", "a")).Reservation!;
        (await reserve.HandleAsync("b_reserve", Punter, 1, "ZAR", "b")).Failure.ShouldBe(WalletFailure.LimitExceeded);

        await TestLedger.Settle(connectionString, time).ReleaseAsync("a_release", reservation.ReservationId, CancellationToken.None);

        (await reserve.HandleAsync("c_reserve", Punter, 3_000, "ZAR", "c")).WasApplied.ShouldBeTrue();
    }

    [Fact]
    public async Task Winnings_earn_loss_headroom_back_and_a_clawback_takes_it_away()
    {
        var (store, connectionString) = await WalletAsync();
        var rules = new FixedRules(Limit(SpendKind.Loss, SpendPeriod.Month, 2_000));
        var reserve = TestLedger.Reserve(connectionString, rules, time);
        var transfers = TestLedger.Transfer(connectionString, rules, time);
        (await reserve.HandleAsync("a_reserve", Punter, 2_000, "ZAR", "a")).WasApplied.ShouldBeTrue();
        (await reserve.HandleAsync("b_reserve", Punter, 500, "ZAR", "b")).Failure.ShouldBe(WalletFailure.LimitExceeded);

        await transfers.CreditAsync("a_win", Punter, 1_000, "ZAR", "a");
        (await reserve.HandleAsync("c_reserve", Punter, 1_000, "ZAR", "c")).WasApplied.ShouldBeTrue();

        await transfers.DebitAsync("a_clawback", Punter, 1_000, "ZAR", "a");
        (await reserve.HandleAsync("d_reserve", Punter, 1, "ZAR", "d")).Failure.ShouldBe(WalletFailure.LimitExceeded);
    }

    [Fact]
    public async Task Excluded_punter_cannot_stake_or_deposit_but_is_still_paid()
    {
        var (store, connectionString) = await WalletAsync();
        var rules = new FixedRules(new GamblingRules([], [new SpendBlock(BlockKind.Exclusion, time.GetUtcNow().AddDays(-1), time.GetUtcNow().AddMonths(6))]));
        var transfers = TestLedger.Transfer(connectionString, rules, time);

        var stake = await TestLedger.Reserve(connectionString, rules, time).HandleAsync("x_reserve", Punter, 100, "ZAR", "x");
        var deposit = await transfers.TopUpAsync("x_topup", Punter, 100, "ZAR", "x");
        var winnings = await transfers.CreditAsync("x_win", Punter, 100, "ZAR", "x");

        (stake.Failure, stake.Detail).ShouldBe((WalletFailure.AccountRestricted, "account excluded"));
        deposit.Failure.ShouldBe(WalletFailure.AccountRestricted);
        winnings.WasApplied.ShouldBeTrue();
    }

    [Fact]
    public async Task A_deposit_refused_at_the_limit_tells_the_customer_once_and_one_within_it_says_nothing()
    {
        var (store, connectionString) = await WalletAsync();
        var notices = new RecordingNotifier();
        var transfers = TestLedger.Transfer(connectionString, new FixedRules(Limit(SpendKind.Deposit, SpendPeriod.Day, 5_000)), time, notices);

        await transfers.TopUpAsync("n1", Punter, 4_000, "ZAR", "n1");
        await transfers.TopUpAsync("n2", Punter, 2_000, "ZAR", "n2");

        var hit = notices.Hits.ShouldHaveSingleItem();
        (hit.Refused, hit.Attempted, hit.Limit.Period).ShouldBe(("deposit", 2_000L, SpendPeriod.Day));
    }

    private sealed class RecordingNotifier : ILimitReachedNotifier
    {
        public List<LimitHit> Hits { get; } = [];

        public Task NotifyAsync(LimitHit hit)
        {
            Hits.Add(hit);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Deposit_limit_caps_top_ups_in_the_period()
    {
        var (store, connectionString) = await WalletAsync();
        var transfers = TestLedger.Transfer(connectionString, new FixedRules(Limit(SpendKind.Deposit, SpendPeriod.Day, 5_000)), time);

        (await transfers.TopUpAsync("t1", Punter, 4_000, "ZAR", "t1")).WasApplied.ShouldBeTrue();
        var refused = await transfers.TopUpAsync("t2", Punter, 2_000, "ZAR", "t2");

        (refused.Failure, refused.Detail).ShouldBe((WalletFailure.LimitExceeded, "deposit limit per day: 1000 left"));
        time.Advance(TimeSpan.FromDays(1));
        (await transfers.TopUpAsync("t3", Punter, 5_000, "ZAR", "t3")).WasApplied.ShouldBeTrue();
    }

    [Fact]
    public async Task A_raise_applies_once_its_cooling_period_has_passed()
    {
        var (store, connectionString) = await WalletAsync();
        var rules = new FixedRules(Limit(SpendKind.Stake, SpendPeriod.Month, 1_000, pending: 5_000, due: time.GetUtcNow().AddHours(24)));
        var reserve = TestLedger.Reserve(connectionString, rules, time);

        (await reserve.HandleAsync("a_reserve", Punter, 2_000, "ZAR", "a")).Failure.ShouldBe(WalletFailure.LimitExceeded);
        time.Advance(TimeSpan.FromHours(24));

        (await reserve.HandleAsync("b_reserve", Punter, 2_000, "ZAR", "b")).WasApplied.ShouldBeTrue();
    }

    [Fact]
    public void Compliance_snapshot_maps_to_wallet_rules()
    {
        var at = time.GetUtcNow();
        var snapshot = new RestrictionsChangedV1(Punter, 3,
            [new MoneyLimit(LimitKind.Loss, LimitPeriod.Week, new Money(9_000, "ZAR"), null, at.AddHours(24))],
            [new Restriction(RestrictionKind.SelfExclusion, at, at.AddMonths(6), "r"), new Restriction(RestrictionKind.NoMarketing, at, null, "r"), new Restriction(RestrictionKind.NoDeposits, at, null, "r")],
            null, null, KycStatus.Verified, at);

        var rules = CompactedGamblingRules.Map(snapshot);

        rules.Limits.ShouldHaveSingleItem().ShouldBe(new SpendLimit(SpendKind.Loss, SpendPeriod.Week, 9_000, null, at.AddHours(24)));
        rules.Blocks.Select(b => b.Kind).ShouldBe([BlockKind.Exclusion, BlockKind.NoDeposits]);
    }

    private async Task<(SqlWalletStore Store, string ConnectionString)> WalletAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("rg_" + Guid.NewGuid().ToString("N")[..10]);
        var result = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true" }]);
        (result is Task<int> task ? await task : (int)result!).ShouldBe(0);
        return (new SqlWalletStore(new SqlServerConnectionFactory(connectionString), time), connectionString);
    }
}
