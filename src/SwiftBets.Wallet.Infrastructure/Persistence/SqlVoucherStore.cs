using Dapper;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Infrastructure.Persistence;

public sealed class SqlVoucherStore(ISqlConnectionFactory connections) : IVoucherStore
{
    private static readonly SqlResources Sql = SqlResources.For<SqlVoucherStore>();

    public Task<Voucher?> GetAsync(Guid voucherId, CancellationToken cancellationToken) => OneAsync("Voucher.Get", new { VoucherId = voucherId }, cancellationToken);

    public Task<Voucher?> FindByHashAsync(byte[] codeHash, CancellationToken cancellationToken) => OneAsync("Voucher.FindByHash", Hash(codeHash), cancellationToken);

    public Task<Voucher?> FindByIssueKeyAsync(string issueKey, CancellationToken cancellationToken) => OneAsync("Voucher.FindByIssueKey", new { IssueKey = issueKey }, cancellationToken);

    public Task<IReadOnlyList<Voucher>> ListForUserAsync(Guid userId, int limit, CancellationToken cancellationToken) =>
        ManyAsync("Voucher.ListForUser", new { UserId = userId, Limit = limit }, cancellationToken);

    public Task<IReadOnlyList<Voucher>> SearchAsync(VoucherSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var parameters = Hash(search.CodeHash);
        parameters.AddDynamicParams(new { search.Last4, search.UserId, search.BatchId, Status = (byte?)search.Status, search.Limit });
        return ManyAsync("Voucher.Search", parameters, cancellationToken);
    }

    public async Task<VoucherBatch> SaveBatchAsync(VoucherBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QuerySingleAsync<BatchRow>(new CommandDefinition(Sql.Get("Voucher.SaveBatch"), new
        {
            batch.BatchId, RequestKey = batch.Key, batch.Count, batch.Amount, batch.Currency, Source = (byte)batch.Source, batch.Note, batch.CreatedBy, batch.CreatedAt,
        }, cancellationToken: cancellationToken))).ToDomain();
    }

    public async Task<VoucherBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<BatchRow>(new CommandDefinition(Sql.Get("Voucher.GetBatch"), new { BatchId = batchId }, cancellationToken: cancellationToken)))?.ToDomain();
    }

    public async Task<IReadOnlyList<VoucherBatch>> ListBatchesAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<BatchRow>(new CommandDefinition(Sql.Get("Voucher.ListBatches"), new { Limit = limit }, cancellationToken: cancellationToken))).Select(r => r.ToDomain())];
    }

    public async Task<DateTimeOffset?> LockedUntilAsync(IReadOnlyList<string> subjects, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<DateTimeOffset?>(new CommandDefinition(Sql.Get("Voucher.LockedUntil"), new { Subjects = subjects, Now = now }, cancellationToken: cancellationToken));
    }

    public async Task RecordFailureAsync(string subject, int maxFailures, DateTimeOffset now, TimeSpan window, TimeSpan lockout, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Voucher.RecordFailure"), new
        {
            Subject = subject, MaxFailures = maxFailures, Now = now, WindowFloor = now - window, LockUntil = now + lockout,
        }, cancellationToken: cancellationToken));
    }

    public async Task ClearFailuresAsync(string subject, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Voucher.ClearFailures"), new { Subject = subject }, cancellationToken: cancellationToken));
    }

    private static DynamicParameters Hash(byte[]? codeHash)
    {
        var parameters = new DynamicParameters();
        parameters.Add("CodeHash", codeHash, System.Data.DbType.Binary, size: 32);
        return parameters;
    }

    private async Task<Voucher?> OneAsync(string query, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<VoucherRow>(new CommandDefinition(Sql.Get(query), parameters, cancellationToken: cancellationToken)))?.ToDomain();
    }

    private async Task<IReadOnlyList<Voucher>> ManyAsync(string query, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<VoucherRow>(new CommandDefinition(Sql.Get(query), parameters, cancellationToken: cancellationToken))).Select(r => r.ToDomain())];
    }
}
