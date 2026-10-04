using Dapper;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Infrastructure.Persistence;

public sealed class SqlBonusStore(ISqlConnectionFactory connections) : IBonusStore
{
    private static readonly SqlResources Sql = SqlResources.For<SqlBonusStore>();

    public async Task<BonusGrant?> ActiveGrantAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<GrantRow>(new CommandDefinition(Sql.Get("Bonus.ActiveGrant"), new { AccountId = accountId }, cancellationToken: cancellationToken)))?.ToDomain();
    }

    public async Task<IReadOnlyList<FreeBet>> AvailableFreeBetsAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<FreeBetRow>(new CommandDefinition(Sql.Get("Bonus.FreeBets"), new { AccountId = accountId, Now = now }, cancellationToken: cancellationToken))).Select(r => r.ToDomain())];
    }

    public async Task<IReadOnlyList<BonusGrant>> GrantsDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<GrantRow>(new CommandDefinition(Sql.Get("Bonus.GrantsDue"), new { Now = now, Limit = limit }, cancellationToken: cancellationToken))).Select(r => r.ToDomain())];
    }

    public async Task<IReadOnlyList<OrphanBonus>> OrphanBonusAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<OrphanBonus>(new CommandDefinition(Sql.Get("Bonus.Orphans"), new { Limit = limit }, cancellationToken: cancellationToken))];
    }

    public async Task<int> ExpireFreeBetsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Bonus.ExpireFreeBets"), new { Now = now }, cancellationToken: cancellationToken));
    }
}
