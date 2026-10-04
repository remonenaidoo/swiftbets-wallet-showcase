using Dapper;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Infrastructure.Persistence;

public sealed class SqlReconciliationStore(ISqlConnectionFactory connections) : IReconciliationStore
{
    /// <summary>Caps each check's rows; a ledger that broken needs a human long before the thousand-and-first drift.</summary>
    public const int DriftLimit = 1000;

    private const int CheckTimeoutSeconds = 600;
    private static readonly SqlResources Sql = SqlResources.For<SqlReconciliationStore>();

    public Task<int> CountPunterAccountsAsync(CancellationToken cancellationToken) =>
        ScalarAsync<int>("Reconcile.CountPunterAccounts", cancellationToken);

    public Task<IReadOnlyList<LedgerDrift>> FindBalanceDriftAsync(CancellationToken cancellationToken) =>
        DriftsAsync("Reconcile.BalanceDrift", cancellationToken);

    public Task<IReadOnlyList<LedgerDrift>> FindReservationDriftAsync(CancellationToken cancellationToken) =>
        DriftsAsync("Reconcile.ReservationDrift", cancellationToken);

    public Task<IReadOnlyList<LedgerDrift>> FindUnbalancedPostingsAsync(CancellationToken cancellationToken) =>
        DriftsAsync("Reconcile.UnbalancedPostings", cancellationToken);

    public Task<IReadOnlyList<LedgerDrift>> FindVoucherDriftAsync(CancellationToken cancellationToken) =>
        DriftsAsync("Reconcile.VoucherDrift", cancellationToken);

    public Task<long> SumAllEntriesAsync(CancellationToken cancellationToken) =>
        ScalarAsync<long>("Reconcile.SumAllEntries", cancellationToken);

    public async Task<LedgerDayTotals> DayTotalsAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<LedgerDayTotals>(new CommandDefinition(Sql.Get("Reconcile.DayTotals"), new { From = from, To = from.AddDays(1) }, cancellationToken: cancellationToken));
    }

    public async Task SaveAsync(ReconciliationReport report, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Reconcile.InsertRun"), new
        {
            report.RunId, report.StartedAt, report.CompletedAt, report.AccountsChecked, DriftCount = report.Drifts.Count,
        }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Reconcile.InsertDrift"), report.Drifts.Select(d => new
        {
            report.RunId, Kind = (byte)d.Kind, d.AccountId, d.PostingId, d.Expected, d.Actual,
        }), transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReconciliationReport?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var run = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(Sql.Get("Reconcile.LatestRun"), cancellationToken: cancellationToken));
        if (run is null)
        {
            return null;
        }

        var drifts = await connection.QueryAsync<DriftRow>(new CommandDefinition(Sql.Get("Reconcile.RunDrifts"), new { run.RunId }, cancellationToken: cancellationToken));
        return new ReconciliationReport(run.RunId, run.StartedAt, run.CompletedAt, run.AccountsChecked, [.. drifts.Select(d => d.ToDomain())]);
    }

    private async Task<T> ScalarAsync<T>(string query, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<T>(new CommandDefinition(Sql.Get(query), commandTimeout: CheckTimeoutSeconds, cancellationToken: cancellationToken)) ?? default!;
    }

    private async Task<IReadOnlyList<LedgerDrift>> DriftsAsync(string query, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DriftRow>(new CommandDefinition(Sql.Get(query), new { Limit = DriftLimit }, commandTimeout: CheckTimeoutSeconds, cancellationToken: cancellationToken));
        return [.. rows.Select(r => r.ToDomain())];
    }

    private sealed record RunRow(Guid RunId, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int AccountsChecked);

    private sealed record DriftRow(byte Kind, Guid? AccountId, Guid? PostingId, long Expected, long Actual)
    {
        public LedgerDrift ToDomain() => new((DriftKind)Kind, AccountId, PostingId, Expected, Actual);
    }
}
