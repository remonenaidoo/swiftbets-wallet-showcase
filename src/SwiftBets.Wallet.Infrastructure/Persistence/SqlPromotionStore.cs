using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Infrastructure.Persistence;

public sealed class SqlPromotionStore(ISqlConnectionFactory connections, TimeProvider time) : IPromotionStore
{
    private static readonly SqlResources Sql = SqlResources.For<SqlPromotionStore>();

    public async Task<IReadOnlyList<PromotionRecord>> ListAsync(CancellationToken cancellationToken) =>
        await RecordsAsync(null, cancellationToken);

    public async Task<PromotionRecord?> GetAsync(Guid promotionId, CancellationToken cancellationToken) =>
        (await RecordsAsync(promotionId, cancellationToken)).SingleOrDefault();

    public async Task SaveAsync(Promotion promotion, bool isNew, PromotionAuditEntry audit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get(isNew ? "Promo.Insert" : "Promo.Update"), Parameters(promotion), transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Promo.InsertAudit"), audit, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PromotionAuditEntry>> AuditAsync(Guid promotionId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<PromotionAuditEntry>(new CommandDefinition(Sql.Get("Promo.Audit"), new { PromotionId = promotionId }, cancellationToken: cancellationToken))];
    }

    public async Task<IReadOnlyList<PromotionAward>> AwardsAsync(Guid promotionId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<AwardRow>(new CommandDefinition(Sql.Get("Promo.Awards"), new { PromotionId = promotionId }, cancellationToken: cancellationToken))).Select(r => r.ToAward())];
    }

    public async Task<IReadOnlyList<Promotion>> RunningAsync(PromotionKind kind, string currency, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<PromotionRow>(new CommandDefinition(Sql.Get("Promo.Running"), new { Kind = (byte)kind, Currency = currency, At = at }, cancellationToken: cancellationToken))).Select(r => r.ToDomain())];
    }

    public Task OptInAsync(Guid promotionId, Guid userId, DateTimeOffset at, CancellationToken cancellationToken) =>
        ExecuteAsync("Promo.OptIn", new { PromotionId = promotionId, UserId = userId, At = at }, cancellationToken);

    public Task OptOutAsync(Guid promotionId, Guid userId, CancellationToken cancellationToken) =>
        ExecuteAsync("Promo.OptOut", new { PromotionId = promotionId, UserId = userId }, cancellationToken);

    public Task<IReadOnlySet<Guid>> OptedInAsync(Guid userId, CancellationToken cancellationToken) => IdsAsync("Promo.OptedIn", userId, cancellationToken);

    public Task<IReadOnlySet<Guid>> AwardedAsync(Guid userId, CancellationToken cancellationToken) => IdsAsync("Promo.Awarded", userId, cancellationToken);

    public async Task<bool> MarketingOptedOutAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql.Get("Promo.MarketingOptedOut"), new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public Task SetMarketingOptedOutAsync(Guid userId, bool optedOut, DateTimeOffset at, CancellationToken cancellationToken) =>
        ExecuteAsync("Promo.SetMarketing", new { UserId = userId, OptedOut = optedOut, At = at }, cancellationToken);

    public async Task<bool> RecordAwardAsync(PromotionAward award, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Promo.InsertAward"), AwardParameters(award), cancellationToken: cancellationToken));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }
    }

    public async Task<bool> AwardFreeBetAsync(PromotionAward award, FreeBet freeBet, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Promo.InsertAward"), AwardParameters(award), transaction, cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Bonus.InsertFreeBet"), new
        {
            freeBet.FreeBetId, freeBet.AccountId, freeBet.PromotionId, freeBet.Amount, freeBet.MinOddsHundredths, freeBet.ExpiresAt, State = (byte)freeBet.State, Now = time.GetUtcNow(),
        }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IsFirstStakeAsync(Guid userId, string reference, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql.Get("Promo.IsFirstStake"), new { UserId = userId, Reference = reference }, cancellationToken: cancellationToken));
    }

    private async Task<IReadOnlyList<PromotionRecord>> RecordsAsync(Guid? promotionId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<PromotionRow>(new CommandDefinition(Sql.Get("Promo.List"), new { PromotionId = promotionId }, cancellationToken: cancellationToken)))
            .Select(r => new PromotionRecord(r.ToDomain(), r.CreatedAt, r.UpdatedAt, r.AwardCount))];
    }

    private async Task<IReadOnlySet<Guid>> IdsAsync(string query, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(Sql.Get(query), new { UserId = userId }, cancellationToken: cancellationToken))).ToHashSet();
    }

    private async Task ExecuteAsync(string query, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get(query), parameters, cancellationToken: cancellationToken));
    }

    private object Parameters(Promotion p) => new
    {
        p.PromotionId, p.Name, Kind = (byte)p.Kind, p.Currency, p.MatchPercent, p.MaxAward, p.MinDeposit, p.WageringMultiplier, p.MinOddsHundredths,
        p.SportsPercent, p.CasinoPercent, p.ValidDays, p.OptInRequired, p.StartsAt, p.EndsAt, p.Terms, p.IsActive, Now = time.GetUtcNow(),
    };

    private static object AwardParameters(PromotionAward a) => new
    {
        a.AwardId, a.PromotionId, a.UserId, a.TriggerKey, Outcome = (byte)a.Outcome, a.Reason, a.Amount, a.AwardedAt,
    };

    private sealed record PromotionRow(
        Guid PromotionId, string Name, byte Kind, string Currency, int MatchPercent, long MaxAward, long MinDeposit, int WageringMultiplier, int MinOddsHundredths,
        byte SportsPercent, byte CasinoPercent, int ValidDays, bool OptInRequired, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Terms, bool IsActive,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int AwardCount)
    {
        public Promotion ToDomain() => new(PromotionId, Name, (PromotionKind)Kind, Currency, MatchPercent, MaxAward, MinDeposit, WageringMultiplier, MinOddsHundredths,
            SportsPercent, CasinoPercent, ValidDays, OptInRequired, StartsAt, EndsAt, Terms, IsActive);
    }

    private sealed record AwardRow(Guid AwardId, Guid PromotionId, Guid UserId, string TriggerKey, byte Outcome, string? Reason, long Amount, DateTimeOffset AwardedAt)
    {
        public PromotionAward ToAward() => new(AwardId, PromotionId, UserId, TriggerKey, (AwardOutcome)Outcome, Reason, Amount, AwardedAt);
    }
}
