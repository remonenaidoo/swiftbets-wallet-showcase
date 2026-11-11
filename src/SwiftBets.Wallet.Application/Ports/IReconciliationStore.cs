using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Application.Ports;

/// <summary>Each check is one statement, so under read committed snapshot an in-flight posting never shows as drift.</summary>
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

/// <summary>One UTC day's money by product, in minor units; the offer figures come from the promotions accounts.</summary>
public sealed record LedgerDayTotals(long SportsStakes, long SportsCredits, long SportsDebits, long CasinoStaked, long CasinoReturned, long RacingStaked, long RacingReturned,
    long BonusGranted = 0, long FreeBetsStaked = 0, long BonusReturned = 0, long BonusConverted = 0, long LottoStaked = 0, long LottoReturned = 0);
