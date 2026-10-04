using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Application.Reconciliation;

/// <summary>
/// Asserts the ledger invariants: every punter balance equals the sum of its entries, reserved funds equal held
/// reservations, voucher liabilities equal open vouchers, every posting balances, and the whole ledger sums to zero. It reports and records drift; it never
/// repairs, because a wrong balance needs a human to decide which side is wrong.
/// </summary>
public sealed class ReconcileLedgerHandler(IReconciliationStore store, TimeProvider time)
{
    public async Task<ReconciliationReport> HandleAsync(CancellationToken cancellationToken)
    {
        var startedAt = time.GetUtcNow();
        var accounts = await store.CountPunterAccountsAsync(cancellationToken);
        var drifts = new List<LedgerDrift>();
        drifts.AddRange(await store.FindBalanceDriftAsync(cancellationToken));
        drifts.AddRange(await store.FindReservationDriftAsync(cancellationToken));
        drifts.AddRange(await store.FindUnbalancedPostingsAsync(cancellationToken));
        drifts.AddRange(await store.FindVoucherDriftAsync(cancellationToken));
        if (await store.SumAllEntriesAsync(cancellationToken) is var total and not 0)
        {
            drifts.Add(new LedgerDrift(DriftKind.LedgerTotal, null, null, 0, total));
        }

        var report = new ReconciliationReport(Guid.CreateVersion7(), startedAt, time.GetUtcNow(), accounts, drifts);
        await store.SaveAsync(report, CancellationToken.None);
        return report;
    }
}
