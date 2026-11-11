using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Infrastructure.Persistence;

namespace SwiftBets.Wallet.Infrastructure.Tests;

/// <summary>The ledger handlers wired the way the host wires them, over one test database.</summary>
internal static class TestLedger
{
    public static PostingRunner Runner(IWalletStore store, ILimitReachedNotifier? limitNotices = null) => new(store, limitNotices ?? new NoLimitNotices());

    public static ReserveFundsHandler Reserve(string connectionString, IGamblingRules rules, TimeProvider time) =>
        new(Runner(Store(connectionString, time)), rules, time);

    public static TransferHandler Transfer(string connectionString, IGamblingRules rules, TimeProvider time, ILimitReachedNotifier? limitNotices = null) =>
        new(Runner(Store(connectionString, time), limitNotices), rules, time, BonusLedger(connectionString, time));

    public static SettleReservationHandler Settle(string connectionString, TimeProvider time)
    {
        var store = Store(connectionString, time);
        return new(Runner(store), store, BonusLedger(connectionString, time));
    }

    public static BonusLedgerHandler BonusLedger(string connectionString, TimeProvider time) =>
        new(Runner(Store(connectionString, time)), new SqlBonusStore(new SqlServerConnectionFactory(connectionString)), time);

    private static SqlWalletStore Store(string connectionString, TimeProvider time) => new(new SqlServerConnectionFactory(connectionString), time);

    private sealed class NoLimitNotices : ILimitReachedNotifier
    {
        public Task NotifyAsync(LimitHit hit) => Task.CompletedTask;
    }
}
