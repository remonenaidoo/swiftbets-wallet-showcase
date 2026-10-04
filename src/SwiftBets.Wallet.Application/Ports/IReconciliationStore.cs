using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Application.Ports;

/// <summary>
/// Read-only checks of the ledger against its balance projections. Each check is one statement, so under read committed
/// snapshot it sees a single committed state and an in-flight posting can never show up as drift.
/// </summary>
public interface IReconciliationStore
{
    Task<int> CountPunterAccountsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LedgerDrift>> FindBalanceDriftAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LedgerDrift>> FindReservationDriftAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LedgerDrift>> FindUnbalancedPostingsAsync(CancellationToken cancellationToken);

    /// <summary>Voucher liability accounts whose entries differ from the vouchers still open.</summary>
    Task<IReadOnlyList<LedgerDrift>> FindVoucherDriftAsync(CancellationToken cancellationToken);

    Task<long> SumAllEntriesAsync(CancellationToken cancellationToken);

    /// <summary>One UTC day of postings in minor units, for the reporting warehouse to reconcile against.</summary>
    Task<LedgerDayTotals> DayTotalsAsync(DateOnly day, CancellationToken cancellationToken);

    Task SaveAsync(ReconciliationReport report, CancellationToken cancellationToken);

    Task<ReconciliationReport?> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sports stakes captured, sports credits and debits (payouts and clawbacks), and casino and racing money staked and returned.
/// The offer figures come from the promotions accounts: bonus granted, free bets staked, money returned to them (forfeits,
/// expiries, kept free bet stakes), and bonus converted to cash (D166). Lotto staked and returned (net of override clawbacks) come last.
/// </summary>
public sealed record LedgerDayTotals(long SportsStakes, long SportsCredits, long SportsDebits, long CasinoStaked, long CasinoReturned, long RacingStaked, long RacingReturned,
    long BonusGranted = 0, long FreeBetsStaked = 0, long BonusReturned = 0, long BonusConverted = 0, long LottoStaked = 0, long LottoReturned = 0);
