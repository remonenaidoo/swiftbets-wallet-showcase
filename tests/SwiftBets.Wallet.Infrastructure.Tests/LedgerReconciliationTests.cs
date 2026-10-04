using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Reconciliation;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Infrastructure.Persistence;

namespace SwiftBets.Wallet.Infrastructure.Tests;

public sealed class LedgerReconciliationTests(SqlServerFixture sql)
{
    private static readonly Guid Punter = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Ledger_that_matches_its_balances_reports_no_drift_and_records_the_run()
    {
        var wallet = await WalletAsync();
        await wallet.ActivityAsync();

        var report = await wallet.Handler.HandleAsync(CancellationToken.None);

        report.IsClean.ShouldBeTrue();
        report.AccountsChecked.ShouldBe(5);
        var latest = (await wallet.Reconciliation.GetLatestAsync(CancellationToken.None))!;
        (latest.RunId, latest.Drifts.Count).ShouldBe((report.RunId, 0));
    }

    [Fact]
    public async Task Balance_changed_outside_the_ledger_is_reported_with_both_sides()
    {
        var wallet = await WalletAsync();
        await wallet.ActivityAsync();
        await wallet.ExecuteAsync("UPDATE wallet.Accounts SET Available = Available + 1 WHERE AccountId = @Punter", new { Punter });

        var report = await wallet.Handler.HandleAsync(CancellationToken.None);

        var drift = report.Drifts.ShouldHaveSingleItem();
        drift.Kind.ShouldBe(DriftKind.AvailableBalance);
        drift.AccountId.ShouldBe(Punter);
        (drift.Actual - drift.Expected).ShouldBe(1);
        (await wallet.Reconciliation.GetLatestAsync(CancellationToken.None))!.Drifts.ShouldBe(report.Drifts);
    }

    [Fact]
    public async Task One_sided_entry_is_reported_as_an_unbalanced_posting_and_a_ledger_total()
    {
        var wallet = await WalletAsync();
        await wallet.ActivityAsync();
        await wallet.ExecuteAsync("""
            INSERT INTO wallet.LedgerEntries (PostingId, AccountId, Bucket, Amount)
            SELECT TOP (1) PostingId, '00000000-0000-0000-0000-00000000b001', 1, 700 FROM wallet.Postings ORDER BY Sequence DESC
            """);

        var report = await wallet.Handler.HandleAsync(CancellationToken.None);

        report.Drifts.Select(d => d.Kind).ShouldBe([DriftKind.UnbalancedPosting, DriftKind.LedgerTotal], ignoreOrder: true);
        report.Drifts.Single(d => d.Kind == DriftKind.LedgerTotal).Actual.ShouldBe(700);
    }

    [Fact]
    public async Task Reservation_released_without_returning_the_funds_is_reported()
    {
        var wallet = await WalletAsync();
        var reservation = (await new ReserveFundsHandler(new PostingRunner(wallet.Store), FixedRules.None, TimeProvider.System).HandleAsync("held_reserve", Punter, 1_500, "ZAR", "held")).Reservation!;
        await wallet.ExecuteAsync("UPDATE wallet.Reservations SET State = 3 WHERE ReservationId = @Id", new { Id = reservation.ReservationId });

        var report = await wallet.Handler.HandleAsync(CancellationToken.None);

        var drift = report.Drifts.ShouldHaveSingleItem();
        (drift.Kind, drift.AccountId, drift.Expected, drift.Actual).ShouldBe((DriftKind.HeldReservations, Punter, 0L, 1_500L));
    }

    [Fact]
    public async Task Postings_committed_while_reconciling_never_show_as_drift()
    {
        var wallet = await WalletAsync();
        var transfers = new TransferHandler(new PostingRunner(wallet.Store), FixedRules.None, TimeProvider.System);
        var writes = Task.WhenAll(Enumerable.Range(0, 40).Select(i => transfers.CreditAsync($"race_{i}", Punter, 100, "ZAR", "race")));

        var reports = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => wallet.Handler.HandleAsync(CancellationToken.None)));
        await writes;

        reports.ShouldAllBe(r => r.IsClean);
    }

    [Fact]
    public async Task Day_totals_split_casino_and_racing_money_from_sportsbook_money_by_their_keys()
    {
        var wallet = await WalletAsync();
        var transfers = new TransferHandler(new PostingRunner(wallet.Store), FixedRules.None, TimeProvider.System);
        await transfers.CreditAsync("casino:sim-seamless:w-1", Punter, 700, "ZAR", "casino win");
        await transfers.CreditAsync("racing:payout:r-1", Punter, 1_300, "ZAR", "racing win");
        await transfers.CreditAsync("payout_c1_v1", Punter, 2_500, "ZAR", "payout");

        var totals = await wallet.Reconciliation.DayTotalsAsync(DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        (totals.CasinoReturned, totals.RacingReturned, totals.SportsCredits).ShouldBe((700L, 1_300L, 2_500L));
    }

    [Fact]
    public async Task Day_totals_count_lotto_stakes_and_returns_net_of_clawbacks()
    {
        var wallet = await WalletAsync();
        var transfers = new TransferHandler(new PostingRunner(wallet.Store), FixedRules.None, TimeProvider.System);
        await transfers.CreditAsync("lotto:win:b-1:1", Punter, 900, "ZAR", "lotto win");
        await transfers.DebitAsync("lotto:bet:b-2", Punter, 300, "ZAR", "lotto bet");
        await transfers.DebitAsync("lotto:clawback:b-1:2", Punter, 400, "ZAR", "lotto override");

        var totals = await wallet.Reconciliation.DayTotalsAsync(DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        (totals.LottoStaked, totals.LottoReturned).ShouldBe((300L, 500L));
    }

    [Fact]
    public async Task Lotto_money_is_not_counted_as_sportsbook_money()
    {
        var wallet = await WalletAsync();
        await new TransferHandler(new PostingRunner(wallet.Store), FixedRules.None, TimeProvider.System).CreditAsync("lotto:win:b-3:1", Punter, 900, "ZAR", "lotto win");

        (await wallet.Reconciliation.DayTotalsAsync(DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None)).SportsCredits.ShouldBe(0);
    }

    [Fact]
    public async Task Another_day_counts_none_of_today()
    {
        var wallet = await WalletAsync();
        await new TransferHandler(new PostingRunner(wallet.Store), FixedRules.None, TimeProvider.System).CreditAsync("payout_c2_v1", Punter, 2_500, "ZAR", "payout");

        (await wallet.Reconciliation.DayTotalsAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), CancellationToken.None)).SportsCredits.ShouldBe(0);
    }

    private async Task<Wallet> WalletAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("recon_" + Guid.NewGuid().ToString("N")[..10]);
        var entryPoint = typeof(Program).Assembly.EntryPoint!;
        var result = entryPoint.Invoke(null, [new[] { $"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true" }]);
        (result is Task<int> task ? await task : (int)result!).ShouldBe(0);
        var connections = new SqlServerConnectionFactory(connectionString);
        var reconciliation = new SqlReconciliationStore(connections);
        var store = new SqlWalletStore(connections, TimeProvider.System);
        return new Wallet(connectionString, store, reconciliation, new ReconcileLedgerHandler(reconciliation, TimeProvider.System));
    }

    private sealed record Wallet(string ConnectionString, SqlWalletStore Store, SqlReconciliationStore Reconciliation, ReconcileLedgerHandler Handler)
    {
        public async Task ActivityAsync()
        {
            var runner = new PostingRunner(Store);
            var reservation = (await new ReserveFundsHandler(runner, FixedRules.None, TimeProvider.System).HandleAsync("a_reserve", Punter, 2_000, "ZAR", "a")).Reservation!;
            await new SettleReservationHandler(runner, Store).CaptureAsync("a_capture", reservation.ReservationId, CancellationToken.None);
            await new TransferHandler(runner, FixedRules.None, TimeProvider.System).CreditAsync("a_win", Punter, 5_000, "ZAR", "a");
            await new TransferHandler(runner, FixedRules.None, TimeProvider.System).DebitAsync("a_clawback", Punter, 1_000, "ZAR", "a");
        }

        public async Task ExecuteAsync(string statement, object? parameters = null)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.ExecuteAsync(statement, parameters);
        }
    }
}
