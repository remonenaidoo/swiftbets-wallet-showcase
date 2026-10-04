using System.Data;
using Dapper;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Infrastructure.Persistence;

public sealed class SqlWalletStore(ISqlConnectionFactory connections, TimeProvider time) : IWalletStore
{
    private static readonly SqlResources Sql = SqlResources.For<SqlWalletStore>();

    public async Task<IWalletTransaction> BeginAsync()
    {
        var connection = await connections.OpenAsync(CancellationToken.None);
        var transaction = (Microsoft.Data.SqlClient.SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        return new SqlWalletTransaction(connection, transaction, time);
    }

    public async Task<Account?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(Sql.Get("Wallet.GetAccount"), new { AccountId = accountId }, cancellationToken: cancellationToken)))?.ToDomain();
    }

    public async Task<Reservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(Sql.Get("Wallet.GetReservation"), new { ReservationId = reservationId }, cancellationToken: cancellationToken)))?.ToDomain();
    }

    public async Task<Reservation?> FindReservationByKeyAsync(string reserveIdempotencyKey, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(Sql.Get("Wallet.FindReservationByKey"), new { IdempotencyKey = reserveIdempotencyKey }, cancellationToken: cancellationToken)))?.ToDomain();
    }

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<AccountRow>(new CommandDefinition(Sql.Get("Wallet.ListAccounts"), new { UserId = userId }, cancellationToken: cancellationToken))).Select(r => r.ToDomain())];
    }

    public async Task<Account?> OpenAccountAsync(Guid accountId, Guid userId, string currency, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Wallet.OpenAccount"), new { AccountId = accountId, UserId = userId, Currency = currency, Now = time.GetUtcNow() }, cancellationToken: cancellationToken));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return null;
        }

        return await GetAccountAsync(accountId, cancellationToken);
    }

    public async Task<IReadOnlyList<StatementLine>> StatementAsync(Guid accountId, long? before, int limit, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<StatementRow>(new CommandDefinition(Sql.Get("Wallet.Statement"), new { AccountId = accountId, Before = before, Limit = limit, From = from, To = to }, cancellationToken: cancellationToken))).Select(r => r.ToLine())];
    }

    public async Task<bool> SetBlacklistedAsync(Guid accountId, bool isBlacklisted, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(Sql.Get("Wallet.SetBlacklisted"), new { AccountId = accountId, IsBlacklisted = isBlacklisted }, cancellationToken: cancellationToken)) == 1;
    }
}
