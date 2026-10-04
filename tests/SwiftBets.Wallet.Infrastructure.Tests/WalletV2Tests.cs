using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.ResponsibleGambling;
using SwiftBets.Wallet.Infrastructure.Persistence;

namespace SwiftBets.Wallet.Infrastructure.Tests;

/// <summary>Currency accounts, deposits, withdrawals and the statement (E3).</summary>
public sealed class WalletV2Tests(SqlServerFixture sql)
{
    private static readonly Guid Punter = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_customer_gets_one_account_per_currency_and_the_first_keeps_the_user_id()
    {
        var (store, _) = await WalletAsync();
        var accounts = new AccountsHandler(store);
        var newcomer = Guid.NewGuid();

        var zar = (await accounts.OpenAsync(newcomer, "ZAR", CancellationToken.None)).Account!;
        var usd = (await accounts.OpenAsync(newcomer, "USD", CancellationToken.None)).Account!;
        var again = (await accounts.OpenAsync(newcomer, "USD", CancellationToken.None)).Account!;

        zar.AccountId.ShouldBe(newcomer);
        usd.AccountId.ShouldNotBe(newcomer);
        again.AccountId.ShouldBe(usd.AccountId);
        usd.UserId.ShouldBe(newcomer);
        (await accounts.ListAsync(newcomer, CancellationToken.None)).Select(a => a.Currency).ShouldBe(["ZAR", "USD"]);
        (await accounts.OpenAsync(newcomer, "EUR", CancellationToken.None)).Failure.ShouldBe(WalletFailure.CurrencyMismatch);
    }

    [Fact]
    public async Task A_customer_who_never_deposited_has_a_zero_rand_balance_not_a_missing_wallet()
    {
        var (store, _) = await WalletAsync();
        var newcomer = Guid.NewGuid();

        var balance = await new AccountsHandler(store).BalanceAsync(newcomer, CancellationToken.None);

        balance.AccountId.ShouldBe(newcomer);
        balance.UserId.ShouldBe(newcomer);
        balance.Currency.ShouldBe("ZAR");
        balance.Available.ShouldBe(0);
        balance.Reserved.ShouldBe(0);
        (await store.ListAccountsAsync(newcomer, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_opened_account_reports_its_real_balance_not_the_zero_default()
    {
        var (store, _) = await WalletAsync();
        var customer = Guid.NewGuid();
        var accounts = new AccountsHandler(store);
        var usd = (await accounts.OpenAsync(customer, "USD", CancellationToken.None)).Account!;
        var transfer = new TransferHandler(new PostingRunner(store), new FixedRules(new GamblingRules([], [])), _time);
        (await transfer.DepositAsync("deposit_balance", usd.AccountId, 2_500, "USD", "pay_balance")).WasApplied.ShouldBeTrue();

        var balance = await accounts.BalanceAsync(customer, CancellationToken.None);

        balance.Currency.ShouldBe("USD");
        balance.Available.ShouldBe(2_500);
    }

    [Fact]
    public async Task Concurrent_first_opens_end_with_one_account_per_currency()
    {
        var (store, _) = await WalletAsync();
        var newcomer = Guid.NewGuid();

        var opened = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => new AccountsHandler(store).OpenAsync(newcomer, i % 2 == 0 ? "ZAR" : "USD", CancellationToken.None)));

        opened.ShouldAllBe(o => o.Failure == null);
        var accounts = await store.ListAccountsAsync(newcomer, CancellationToken.None);
        accounts.Count.ShouldBe(2);
        accounts.ShouldContain(a => a.AccountId == newcomer);
    }

    [Fact]
    public async Task A_dollar_deposit_comes_from_the_dollar_funding_account_and_a_rand_limit_does_not_cap_it()
    {
        var (store, connectionString) = await WalletAsync();
        var usd = (await new AccountsHandler(store).OpenAsync(Punter, "USD", CancellationToken.None)).Account!;
        var rules = new FixedRules(new GamblingRules([new SpendLimit(SpendKind.Deposit, SpendPeriod.Day, 1_000, null, null, "ZAR")], []));
        var transfer = new TransferHandler(new PostingRunner(store), rules, _time);

        var deposit = await transfer.DepositAsync("deposit_1", usd.AccountId, 50_000, "USD", "pay_1");

        deposit.WasApplied.ShouldBeTrue();
        (await transfer.DepositAsync("deposit_2", Punter, 2_000, "ZAR", "pay_2")).Failure.ShouldBe(WalletFailure.LimitExceeded);
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT SUM(Amount) FROM wallet.LedgerEntries WHERE AccountId = '00000000-0000-0000-0000-00000000f840'")).ShouldBe(-50_000);
        await AssertBalancedAsync(connectionString);
    }

    [Fact]
    public async Task A_deposit_into_an_account_that_was_never_opened_is_refused()
    {
        var (store, _) = await WalletAsync();

        (await new TransferHandler(new PostingRunner(store), FixedRules.None, _time).DepositAsync("deposit_x", Guid.NewGuid(), 1_000, "ZAR", "pay_x"))
            .Failure.ShouldBe(WalletFailure.AccountNotFound);
    }

    [Fact]
    public async Task A_withdrawal_is_held_then_paid_to_funding_and_never_counts_as_a_stake()
    {
        var (store, connectionString) = await WalletAsync();
        var rules = new FixedRules(new GamblingRules([new SpendLimit(SpendKind.Stake, SpendPeriod.Day, 1_000, null, null)], []));
        var hold = await new ReserveFundsHandler(new PostingRunner(store), rules, _time).HoldWithdrawalAsync("wd_1_hold", Punter, 30_000, "ZAR", "wd_1");
        var settle = new SettleReservationHandler(new PostingRunner(store), store);

        hold.WasApplied.ShouldBeTrue();
        hold.Reservation!.Purpose.ShouldBe(ReservationPurpose.Withdrawal);
        (await settle.CaptureAsync("wd_1_capture", hold.Reservation.ReservationId, CancellationToken.None)).Failure.ShouldBe(WalletFailure.InvalidState);
        (await settle.CompleteWithdrawalAsync("wd_1_paid", hold.Reservation.ReservationId, CancellationToken.None)).WasApplied.ShouldBeTrue();

        var account = (await store.GetAccountAsync(Punter, CancellationToken.None))!;
        (account.Available, account.Reserved).ShouldBe((70_000L, 0L));
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(Staked), 0) FROM wallet.SpendCounters")).ShouldBe(0);
        (await connection.ExecuteScalarAsync<long>("SELECT SUM(Amount) FROM wallet.LedgerEntries WHERE AccountId = '00000000-0000-0000-0000-00000000f001'")).ShouldBe(-500_000 + 30_000);
        await AssertBalancedAsync(connectionString);
    }

    [Fact]
    public async Task A_withdrawal_block_stops_a_withdrawal_but_an_exclusion_does_not()
    {
        var (store, _) = await WalletAsync();
        var since = _time.GetUtcNow().AddDays(-1);
        var blocked = new FixedRules(new GamblingRules([], [new SpendBlock(BlockKind.NoWithdrawals, since, null)]));
        var excluded = new FixedRules(new GamblingRules([], [new SpendBlock(BlockKind.Exclusion, since, null)]));

        var refused = await new ReserveFundsHandler(new PostingRunner(store), blocked, _time).HoldWithdrawalAsync("wd_2_hold", Punter, 1_000, "ZAR", "wd_2");
        var allowed = await new ReserveFundsHandler(new PostingRunner(store), excluded, _time).HoldWithdrawalAsync("wd_3_hold", Punter, 1_000, "ZAR", "wd_3");

        refused.Failure.ShouldBe(WalletFailure.AccountRestricted);
        allowed.WasApplied.ShouldBeTrue();
    }

    [Fact]
    public async Task A_returned_withdrawal_goes_back_to_available_and_a_stake_cannot_be_paid_out()
    {
        var (store, connectionString) = await WalletAsync();
        var reserve = new ReserveFundsHandler(new PostingRunner(store), FixedRules.None, _time);
        var settle = new SettleReservationHandler(new PostingRunner(store), store);
        var withdrawal = (await reserve.HoldWithdrawalAsync("wd_4_hold", Punter, 5_000, "ZAR", "wd_4")).Reservation!;
        var stake = (await reserve.HandleAsync("c9_reserve", Punter, 1_000, "ZAR", "c9")).Reservation!;

        (await settle.ReleaseAsync("wd_4_release", withdrawal.ReservationId, CancellationToken.None)).WasApplied.ShouldBeTrue();
        (await settle.CompleteWithdrawalAsync("c9_paid", stake.ReservationId, CancellationToken.None)).Failure.ShouldBe(WalletFailure.InvalidState);

        (await store.GetAccountAsync(Punter, CancellationToken.None))!.Available.ShouldBe(99_000);
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<byte>("SELECT Kind FROM wallet.Postings WHERE IdempotencyKey = 'wd_4_release'")).ShouldBe((byte)PostingKind.WithdrawalReturned);
        await AssertBalancedAsync(connectionString);
    }

    [Fact]
    public async Task Statement_lists_available_movements_newest_first_with_the_running_balance_and_pages()
    {
        var (store, _) = await WalletAsync();
        var transfer = new TransferHandler(new PostingRunner(store), FixedRules.None, _time);
        var reserve = new ReserveFundsHandler(new PostingRunner(store), FixedRules.None, _time);
        await transfer.DepositAsync("deposit_s1", Punter, 5_000, "ZAR", "pay_s1");
        await reserve.HandleAsync("cs_reserve", Punter, 2_000, "ZAR", "cs");
        await reserve.HoldWithdrawalAsync("wds_hold", Punter, 1_000, "ZAR", "wds");

        var first = await store.StatementAsync(Punter, null, 2, null, null, CancellationToken.None);
        var rest = await store.StatementAsync(Punter, first[^1].Sequence, 10, null, null, CancellationToken.None);

        first.Select(l => (l.Kind, l.Amount, l.AvailableAfter)).ShouldBe([(PostingKind.WithdrawalHold, -1_000L, 102_000L), (PostingKind.Reserve, -2_000L, 103_000L)]);
        rest.Select(l => l.Kind).ShouldBe([PostingKind.Deposit, PostingKind.TopUp]);
        rest[0].AvailableAfter.ShouldBe(105_000);
    }

    [Fact]
    public async Task A_date_range_keeps_only_movements_inside_it_with_the_running_balance_intact()
    {
        var (store, _) = await WalletAsync();
        var transfer = new TransferHandler(new PostingRunner(store), FixedRules.None, _time);
        await transfer.DepositAsync("deposit_r1", Punter, 5_000, "ZAR", "pay_r1");
        var now = _time.GetUtcNow();

        var inside = await store.StatementAsync(Punter, null, 10, now.AddMinutes(-1), now.AddMinutes(1), CancellationToken.None);
        var before = await store.StatementAsync(Punter, null, 10, now.AddDays(-2), now.AddDays(-1), CancellationToken.None);

        inside.Select(l => (l.Kind, l.AvailableAfter)).ShouldContain((PostingKind.Deposit, 105_000L));
        before.ShouldBeEmpty();
    }

    private async Task<(SqlWalletStore Store, string ConnectionString)> WalletAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("v2_" + Guid.NewGuid().ToString("N")[..10]);
        var result = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true" }]);
        (result is Task<int> task ? await task : (int)result!).ShouldBe(0);
        return (new SqlWalletStore(new SqlServerConnectionFactory(connectionString), _time), connectionString);
    }

    private static async Task AssertBalancedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT SUM(Amount) FROM wallet.LedgerEntries")).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM wallet.Accounts a
            WHERE a.Kind = 1 AND (
              a.Available <> (SELECT COALESCE(SUM(Amount), 0) FROM wallet.LedgerEntries e WHERE e.AccountId = a.AccountId AND e.Bucket = 1)
              OR a.Reserved <> (SELECT COALESCE(SUM(Amount), 0) FROM wallet.LedgerEntries e WHERE e.AccountId = a.AccountId AND e.Bucket = 2))
            """)).ShouldBe(0);
    }
}
