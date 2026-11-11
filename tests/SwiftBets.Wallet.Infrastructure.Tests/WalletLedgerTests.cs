using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Infrastructure.Persistence;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace SwiftBets.Wallet.Infrastructure.Tests;

public sealed class WalletLedgerTests(SqlServerFixture sql)
{
    private static readonly Guid Punter = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Concurrent_reserves_with_one_key_debit_exactly_once()
    {
        var (store, connectionString) = await WalletAsync();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            TestLedger.Reserve(connectionString, FixedRules.None, TimeProvider.System).HoldStakeAsync("coupon-1_reserve", Punter, 2_500, "ZAR", "coupon-1")));

        outcomes.Count(o => o.WasApplied).ShouldBe(1);
        outcomes.ShouldAllBe(o => o.Failure == null && o.Reservation!.ReservationId == outcomes[0].Reservation!.ReservationId);
        var account = (await store.GetAccountAsync(Punter, CancellationToken.None))!;
        (account.Available, account.Reserved).ShouldBe((97_500L, 2_500L));
        await AssertLedgerMatchesBalancesAsync(connectionString);
    }

    [Fact]
    public async Task Releasing_a_captured_reservation_is_refused()
    {
        var (store, connectionString) = await WalletAsync();
        var reservation = (await TestLedger.Reserve(connectionString, FixedRules.None, TimeProvider.System).HoldStakeAsync("c2_reserve", Punter, 1_000, "ZAR", "c2")).Reservation!;
        var settle = TestLedger.Settle(connectionString, TimeProvider.System);
        (await settle.CaptureAsync("c2_capture", reservation.ReservationId, CancellationToken.None)).WasApplied.ShouldBeTrue();

        (await settle.ReleaseAsync("c2_release", reservation.ReservationId, CancellationToken.None)).Failure.ShouldBe(WalletFailure.InvalidState);

        (await store.GetAccountAsync(Punter, CancellationToken.None))!.Available.ShouldBe(99_000);
        await AssertLedgerMatchesBalancesAsync(connectionString);
    }

    [Fact]
    public async Task Top_up_opens_a_new_punter_account()
    {
        var (store, connectionString) = await WalletAsync();
        var newcomer = Guid.NewGuid();

        var outcome = await TestLedger.Transfer(connectionString, FixedRules.None, TimeProvider.System).TopUpAsync("topup-1", newcomer, 5_000, "ZAR", "welcome");

        outcome.WasApplied.ShouldBeTrue();
        (await store.GetAccountAsync(newcomer, CancellationToken.None))!.Available.ShouldBe(5_000);
        await AssertLedgerMatchesBalancesAsync(connectionString);
    }

    [Fact]
    public async Task Credit_to_an_unknown_account_is_refused()
    {
        var (store, connectionString) = await WalletAsync();

        var outcome = await TestLedger.Transfer(connectionString, FixedRules.None, TimeProvider.System).CreditAsync("win-1", Guid.NewGuid(), 5_000, "ZAR", "coupon");

        outcome.Failure.ShouldBe(WalletFailure.AccountNotFound);
    }

    [Fact]
    public async Task Blacklisted_punter_receives_no_credit()
    {
        var (store, connectionString) = await WalletAsync();
        await store.SetBlacklistedAsync(Punter, true, CancellationToken.None);

        (await TestLedger.Transfer(connectionString, FixedRules.None, TimeProvider.System).CreditAsync("win-2", Punter, 5_000, "ZAR", "coupon")).Failure.ShouldBe(WalletFailure.AccountBlacklisted);
    }

    [Fact]
    public async Task Lifting_the_blacklist_lets_the_credit_through()
    {
        var (store, connectionString) = await WalletAsync();
        await store.SetBlacklistedAsync(Punter, true, CancellationToken.None);
        await store.SetBlacklistedAsync(Punter, false, CancellationToken.None);

        (await TestLedger.Transfer(connectionString, FixedRules.None, TimeProvider.System).CreditAsync("win-3", Punter, 5_000, "ZAR", "coupon")).WasApplied.ShouldBeTrue();
    }

    private async Task<(SqlWalletStore Store, string ConnectionString)> WalletAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("wallet_" + Guid.NewGuid().ToString("N")[..10]);
        var entryPoint = typeof(Program).Assembly.EntryPoint!;
        var result = entryPoint.Invoke(null, [new[] { $"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true" }]);
        var exit = result is Task<int> task ? await task : (int)result!;
        exit.ShouldBe(0);
        return (new SqlWalletStore(new SqlServerConnectionFactory(connectionString), TimeProvider.System), connectionString);
    }

    private static async Task AssertLedgerMatchesBalancesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        var drift = await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM wallet.Accounts a
            WHERE a.Kind = 1 AND (
              a.Available <> (SELECT COALESCE(SUM(Amount), 0) FROM wallet.LedgerEntries e WHERE e.AccountId = a.AccountId AND e.Bucket = 1)
              OR a.Reserved <> (SELECT COALESCE(SUM(Amount), 0) FROM wallet.LedgerEntries e WHERE e.AccountId = a.AccountId AND e.Bucket = 2))
            """);
        drift.ShouldBe(0);
        (await connection.ExecuteScalarAsync<long>("SELECT SUM(Amount) FROM wallet.LedgerEntries")).ShouldBe(0);
    }
}
