using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Infrastructure.Persistence;

internal sealed class SqlWalletTransaction(SqlConnection connection, SqlTransaction transaction, TimeProvider time) : IWalletTransaction
{
    private static readonly SqlResources Sql = SqlResources.For<SqlWalletTransaction>();

    public async Task<StoredPosting?> FindPostingAsync(string idempotencyKey)
    {
        var row = await connection.QuerySingleOrDefaultAsync<PostingRow>(Sql.Get("Wallet.FindPosting"), new { IdempotencyKey = idempotencyKey }, transaction);
        return row is null ? null : new StoredPosting(row.PostingId, (PostingKind)row.Kind, row.AccountId, row.Amount, row.ReservationId);
    }

    public async Task<Account?> LockAccountAsync(Guid accountId) =>
        (await connection.QuerySingleOrDefaultAsync<AccountRow>(Sql.Get("Wallet.LockAccount"), new { AccountId = accountId }, transaction))?.ToDomain();

    public async Task<Reservation?> LockReservationAsync(Guid reservationId) =>
        (await connection.QuerySingleOrDefaultAsync<ReservationRow>(Sql.Get("Wallet.LockReservation"), new { ReservationId = reservationId }, transaction))?.ToDomain();

    public async Task<Account> OpenPunterAccountAsync(Guid accountId, string currency)
    {
        await connection.ExecuteAsync(Sql.Get("Wallet.OpenPunterAccount"), new { AccountId = accountId, Currency = currency, Now = time.GetUtcNow() }, transaction);
        return (await LockAccountAsync(accountId))!;
    }

    public async Task SaveAsync(Posting posting, StoredPosting record, IReadOnlyList<Account> changedAccounts, Reservation? reservation, bool isNewReservation)
    {
        var now = time.GetUtcNow();
        try
        {
            await connection.ExecuteAsync(Sql.Get("Wallet.InsertPosting"), new
            {
                posting.PostingId, Kind = (byte)posting.Kind, posting.IdempotencyKey, record.AccountId, record.Amount, record.ReservationId, posting.Reference, PostedAt = now,
            }, transaction);
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new DuplicateIdempotencyKeyException(posting.IdempotencyKey, ex);
        }

        if (reservation is not null && isNewReservation)
        {
            await connection.ExecuteAsync(Sql.Get("Wallet.InsertReservation"), new
            {
                reservation.ReservationId, reservation.AccountId, reservation.Amount, reservation.Currency, reservation.Reference, State = (byte)reservation.State, Purpose = (byte)reservation.Purpose,
                reservation.BonusAmount, reservation.FreeBetId, Now = now,
            }, transaction);
        }
        else if (reservation is not null)
        {
            await connection.ExecuteAsync(Sql.Get("Wallet.UpdateReservation"), new { reservation.ReservationId, State = (byte)reservation.State, Now = now }, transaction);
        }

        await connection.ExecuteAsync(Sql.Get("Wallet.InsertEntry"), posting.Entries.Select(e => new { posting.PostingId, e.AccountId, Bucket = (byte)e.Bucket, e.Amount }), transaction);
        await connection.ExecuteAsync(Sql.Get("Wallet.UpdateAccount"), changedAccounts.Select(a => new { a.AccountId, a.Available, a.Reserved, a.Bonus }), transaction);
    }

    public async Task<SpendCounters> GetSpendAsync(Guid accountId, DateTimeOffset now)
    {
        var rows = (await connection.QueryAsync<SpendRow>(Sql.Get("Spend.Get"), Periods(accountId, now), transaction)).ToDictionary(r => (SpendPeriod)r.Period, r => r.ToTotals());
        return new SpendCounters(
            rows.GetValueOrDefault(SpendPeriod.Day, SpendTotals.Zero),
            rows.GetValueOrDefault(SpendPeriod.Week, SpendTotals.Zero),
            rows.GetValueOrDefault(SpendPeriod.Month, SpendTotals.Zero));
    }

    public Task AddSpendAsync(Guid accountId, DateTimeOffset at, long staked, long won, long deposited)
    {
        var parameters = new DynamicParameters(Periods(accountId, at));
        parameters.AddDynamicParams(new { Staked = staked, Won = won, Deposited = deposited });
        return connection.ExecuteAsync(Sql.Get("Spend.Add"), parameters, transaction);
    }

    public Task<DateTimeOffset> ReservedAtAsync(Guid reservationId) =>
        connection.QuerySingleAsync<DateTimeOffset>(Sql.Get("Wallet.ReservedAt"), new { ReservationId = reservationId }, transaction);

    public async Task<BonusGrant?> ActiveGrantAsync(Guid accountId) =>
        (await connection.QuerySingleOrDefaultAsync<GrantRow>(Sql.Get("Bonus.ActiveGrant"), new { AccountId = accountId }, transaction))?.ToDomain();

    public async Task<BonusGrant?> GetGrantAsync(Guid grantId) =>
        (await connection.QuerySingleOrDefaultAsync<GrantRow>(Sql.Get("Bonus.GetGrant"), new { GrantId = grantId }, transaction))?.ToDomain();

    public Task SaveGrantAsync(BonusGrant grant, bool isNew) =>
        connection.ExecuteAsync(Sql.Get(isNew ? "Bonus.InsertGrant" : "Bonus.UpdateGrant"), new
        {
            grant.GrantId, grant.AccountId, grant.PromotionId, grant.Amount, grant.WageringTarget, grant.Wagered, grant.MinOddsHundredths,
            grant.SportsPercent, grant.CasinoPercent, grant.ExpiresAt, State = (byte)grant.State, Now = time.GetUtcNow(),
        }, transaction);

    public async Task<FreeBet?> LockFreeBetAsync(Guid freeBetId) =>
        (await connection.QuerySingleOrDefaultAsync<FreeBetRow>(Sql.Get("Bonus.LockFreeBet"), new { FreeBetId = freeBetId }, transaction))?.ToDomain();

    public Task SaveFreeBetAsync(FreeBet freeBet, bool isNew) =>
        connection.ExecuteAsync(Sql.Get(isNew ? "Bonus.InsertFreeBet" : "Bonus.UpdateFreeBet"), new
        {
            freeBet.FreeBetId, freeBet.AccountId, freeBet.PromotionId, freeBet.Amount, freeBet.MinOddsHundredths, freeBet.ExpiresAt, State = (byte)freeBet.State, Now = time.GetUtcNow(),
        }, transaction);

    public async Task<BonusStake?> FindStakeAsync(Guid accountId, string reference) =>
        (await connection.QuerySingleOrDefaultAsync<StakeRow>(Sql.Get("Bonus.FindStake"), new { AccountId = accountId, Reference = reference }, transaction))?.ToDomain();

    public async Task<BonusStake?> LatestCasinoStakeAsync(Guid accountId) =>
        (await connection.QuerySingleOrDefaultAsync<StakeRow>(Sql.Get("Bonus.LatestCasinoStake"), new { AccountId = accountId }, transaction))?.ToDomain();

    public Task SaveStakeAsync(BonusStake stake, bool isNew) =>
        connection.ExecuteAsync(Sql.Get(isNew ? "Bonus.InsertStake" : "Bonus.UpdateStakeRetained"), new
        {
            stake.StakeKey, stake.AccountId, stake.Reference, Product = (byte)stake.Product, stake.OddsHundredths, stake.CashAmount, stake.BonusAmount,
            stake.GrantId, stake.FreeBetId, stake.FreeBetAmount, stake.FreeBetRetained, Now = time.GetUtcNow(),
        }, transaction);

    public Task CommitAsync() => transaction.CommitAsync();

    private static object Periods(Guid accountId, DateTimeOffset at) => new
    {
        AccountId = accountId,
        Day = SpendPeriods.StartOf(SpendPeriod.Day, at).ToDateTime(TimeOnly.MinValue),
        Week = SpendPeriods.StartOf(SpendPeriod.Week, at).ToDateTime(TimeOnly.MinValue),
        Month = SpendPeriods.StartOf(SpendPeriod.Month, at).ToDateTime(TimeOnly.MinValue),
    };

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
