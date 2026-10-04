using Microsoft.Extensions.Time.Testing;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Application.Reconciliation;
using SwiftBets.Wallet.Application.Vouchers;
using SwiftBets.Wallet.Domain.Vouchers;
using SwiftBets.Wallet.Infrastructure.Persistence;

namespace SwiftBets.Wallet.Infrastructure.Tests;

/// <summary>Cash vouchers (D168): bought from a balance, redeemed once, voided back, guarded against guessing.</summary>
public sealed class VoucherTests(SqlServerFixture sql)
{
    private static readonly Guid Buyer = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Friend = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_bought_voucher_is_redeemed_once_when_two_customers_race_and_the_ledger_reconciles()
    {
        var (handler, store, connectionString) = await VouchersAsync();
        var bought = await handler.BuyAsync(Buyer, "k1", 10_000, "ZAR", null, CancellationToken.None);

        var raced = await Task.WhenAll(
            handler.RedeemAsync(Friend, bought.Code, "ip:a", CancellationToken.None),
            handler.RedeemAsync(Stranger, bought.Code, "ip:b", CancellationToken.None));

        raced.Count(r => r.Voucher is not null).ShouldBe(1);
        raced.Single(r => r.Voucher is null).Error.ShouldBe("voucher_used");
        (await store.GetAccountAsync(Buyer, CancellationToken.None))!.Available.ShouldBe(90_000);
        var report = await new ReconcileLedgerHandler(new SqlReconciliationStore(new SqlServerConnectionFactory(connectionString)), _time).HandleAsync(CancellationToken.None);
        report.Drifts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeating_a_purchase_with_the_same_key_returns_the_same_code_and_takes_the_money_once()
    {
        var (handler, store, _) = await VouchersAsync();

        var first = await handler.BuyAsync(Buyer, "same", 5_000, "ZAR", null, CancellationToken.None);
        var again = await handler.BuyAsync(Buyer, "same", 5_000, "ZAR", null, CancellationToken.None);

        again.Code.ShouldBe(first.Code);
        (await store.GetAccountAsync(Buyer, CancellationToken.None))!.Available.ShouldBe(95_000);
    }

    [Fact]
    public async Task Wrong_codes_lock_the_customer_out_even_before_a_right_one()
    {
        var (handler, _, _) = await VouchersAsync();
        var bought = await handler.BuyAsync(Buyer, "k2", 5_000, "ZAR", null, CancellationToken.None);

        for (var i = 0; i < 5; i++)
        {
            (await handler.RedeemAsync(Friend, "AAAA-AAAA-AAAA-AAAA", "ip:c", CancellationToken.None)).Error.ShouldBe("invalid_code");
        }

        var blocked = await handler.RedeemAsync(Friend, bought.Code, "ip:c", CancellationToken.None);
        blocked.Error.ShouldBe("too_many_attempts");
        blocked.RetryAfter.ShouldBe(_time.GetUtcNow().AddHours(1));
    }

    [Fact]
    public async Task Voiding_returns_a_bought_voucher_to_the_buyer_and_a_spent_one_cannot_be_voided()
    {
        var (handler, store, _) = await VouchersAsync();
        var open = await handler.BuyAsync(Buyer, "k3", 5_000, "ZAR", null, CancellationToken.None);
        var spent = await handler.BuyAsync(Buyer, "k4", 5_000, "ZAR", null, CancellationToken.None);
        await handler.RedeemAsync(Friend, spent.Code, "ip:d", CancellationToken.None);

        (await handler.VoidAsync(open.Voucher!.VoucherId, "op", "lost", CancellationToken.None)).Voucher!.Status.ShouldBe(VoucherStatus.Voided);
        (await handler.VoidAsync(spent.Voucher!.VoucherId, "op", "late", CancellationToken.None)).Error.ShouldBe("invalid_state");
        (await store.GetAccountAsync(Buyer, CancellationToken.None))!.Available.ShouldBe(95_000);
    }

    [Fact]
    public async Task A_retried_batch_finishes_with_the_same_codes_and_an_expired_one_is_refused()
    {
        var (handler, _, _) = await VouchersAsync();

        var first = (await handler.IssueBatchAsync("op", "batch-1", 3, 2_000, "ZAR", VoucherSource.Retail, "Shop 1", CancellationToken.None)).Batch!;
        var again = (await handler.IssueBatchAsync("op", "batch-1", 3, 2_000, "ZAR", VoucherSource.Retail, "Shop 1", CancellationToken.None)).Batch!;
        _time.Advance(TimeSpan.FromDays(366));

        again.Vouchers.Select(v => v.Code).ShouldBe(first.Vouchers.Select(v => v.Code));
        (await handler.RedeemAsync(Friend, first.Vouchers[0].Code, "ip:e", CancellationToken.None)).Error.ShouldBe("voucher_expired");
    }

    private async Task<(VoucherHandler Handler, SqlWalletStore Store, string ConnectionString)> VouchersAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("vch_" + Guid.NewGuid().ToString("N")[..10]);
        var result = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true" }]);
        (result is Task<int> task ? await task : (int)result!).ShouldBe(0);
        var factory = new SqlServerConnectionFactory(connectionString);
        var store = new SqlWalletStore(factory, _time);
        var settings = new VoucherSettings("test-secret");
        var handler = new VoucherHandler(new PostingRunner(store), store, new SqlVoucherStore(factory), new AccountsHandler(store), FixedRules.None,
            new VoucherCodes(settings), settings, new NoGifts(), _time);
        return (handler, store, connectionString);
    }

    private sealed class NoGifts : IVoucherGiftNotifier
    {
        public Task SendAsync(Voucher voucher, string code) => Task.CompletedTask;
    }
}
