using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Testing;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace SwiftBets.Wallet.Migrator.Tests;

public sealed class MigratorTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Migrator_creates_the_schema_and_is_idempotent()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        string[] args = [$"--ConnectionStrings:SbWallet={connectionString}"];

        (await RunAsync(args)).ShouldBe(0);
        (await RunAsync(args)).ShouldBe(0);

        await using var connection = new SqlConnection(connectionString);
        var tables = (await connection.QueryAsync<string>("SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id")).ToList();
        tables.ShouldContain("inbox.ProcessedMessages");
        tables.ShouldContain("outbox.Messages");
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name = 'wallet'")).ShouldBe(1);
    }

    [Fact]
    public async Task Reconciliation_migration_rolls_back_and_reapplies()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        string[] args = [$"--ConnectionStrings:SbWallet={connectionString}"];
        (await RunAsync(args)).ShouldBe(0);
        await using var connection = new SqlConnection(connectionString);

        await connection.ExecuteAsync(Rollback("0004_reconciliation"));

        (await ReconciliationTablesAsync(connection)).ShouldBe(0);
        (await RunAsync(args)).ShouldBe(0);
        (await ReconciliationTablesAsync(connection)).ShouldBe(2);
    }

    [Fact]
    public async Task Demo_punters_exist_only_when_seeding_is_asked_for()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        string[] plain = [$"--ConnectionStrings:SbWallet={connectionString}"];
        string[] seeded = [.. plain, "--Migrator:SeedDemo=true"];
        await using var connection = new SqlConnection(connectionString);

        (await RunAsync(plain)).ShouldBe(0);
        (await PuntersAsync(connection)).ShouldBe(0);

        (await RunAsync(seeded)).ShouldBe(0);
        (await RunAsync(seeded)).ShouldBe(0);
        (await PuntersAsync(connection)).ShouldBe(5);
        (await connection.ExecuteScalarAsync<long>("SELECT SUM(Amount) FROM wallet.LedgerEntries")).ShouldBe(0);
    }

    [Fact]
    public async Task Currency_accounts_roll_back_while_customers_hold_one_account_and_refuse_otherwise()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        string[] args = [$"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true"];
        (await RunAsync(args)).ShouldBe(0);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync("INSERT INTO wallet.Accounts (AccountId, Kind, Currency, UserId, CreatedAt) VALUES (NEWID(), 1, 'USD', '10000000-0000-0000-0000-000000000001', SYSUTCDATETIME())");
        await connection.ExecuteAsync(Rollback("0009_bonus_wagering_and_promotions"));
        await connection.ExecuteAsync(Rollback("0008_bonus_and_withdrawals"));

        await Should.ThrowAsync<SqlException>(() => connection.ExecuteAsync(Rollback("0007_currency_accounts")));

        await connection.ExecuteAsync("DELETE FROM wallet.Accounts WHERE Currency = 'USD' AND Kind = 1");
        await connection.ExecuteAsync(Rollback("0007_currency_accounts"));
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('wallet.Accounts') AND name IN ('UserId', 'Bonus')")).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wallet.Accounts WHERE Currency = 'USD'")).ShouldBe(0);
    }

    [Fact]
    public async Task Removing_the_seed_keeps_demo_punters_that_have_been_used()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        string[] seeded = [$"--ConnectionStrings:SbWallet={connectionString}", "--Migrator:SeedDemo=true"];
        (await RunAsync(seeded)).ShouldBe(0);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync("UPDATE wallet.Accounts SET Available = Available - 1 WHERE AccountId = '10000000-0000-0000-0000-000000000001'");
        await connection.ExecuteAsync("DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%0005_demo_seed_removed.sql'");

        (await RunAsync([$"--ConnectionStrings:SbWallet={connectionString}"])).ShouldBe(0);
        (await PuntersAsync(connection)).ShouldBe(1);

        // Rollbacks run newest first, so the later schema changes come off before the seed is restored.
        await connection.ExecuteAsync(Rollback("0009_bonus_wagering_and_promotions"));
        await connection.ExecuteAsync(Rollback("0008_bonus_and_withdrawals"));
        await connection.ExecuteAsync(Rollback("0007_currency_accounts"));
        await connection.ExecuteAsync(Rollback("0005_demo_seed_removed"));
        (await PuntersAsync(connection)).ShouldBe(5);
    }

    [Fact]
    public async Task Promotions_roll_back_only_until_promotions_money_has_moved()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        (await RunAsync([$"--ConnectionStrings:SbWallet={connectionString}"])).ShouldBe(0);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync("""
            INSERT INTO wallet.Postings (PostingId, Kind, IdempotencyKey, AccountId, Amount, Reference, PostedAt) VALUES ('00000000-0000-0000-0000-0000000000aa', 11, 'k', '00000000-0000-0000-0000-00000000c001', 1, 'r', SYSUTCDATETIME());
            INSERT INTO wallet.LedgerEntries (PostingId, AccountId, Bucket, Amount) VALUES ('00000000-0000-0000-0000-0000000000aa', '00000000-0000-0000-0000-00000000c001', 1, -1);
            """);

        await Should.ThrowAsync<SqlException>(() => connection.ExecuteAsync(Rollback("0009_bonus_wagering_and_promotions")));

        await connection.ExecuteAsync("DELETE FROM wallet.LedgerEntries; DELETE FROM wallet.Postings;");
        await connection.ExecuteAsync(Rollback("0009_bonus_wagering_and_promotions"));
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wallet.Accounts WHERE Kind = 4")).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_connection_string_fails_with_a_usage_code() =>
        (await RunAsync([])).ShouldBe(2);

    private static Task<int> PuntersAsync(SqlConnection connection) =>
        connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wallet.Accounts WHERE Kind = 1");

    private static Task<int> ReconciliationTablesAsync(SqlConnection connection) =>
        connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('wallet') AND name LIKE 'Reconciliation%'");

    private static string Rollback(string migration)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream($"SwiftBets.Wallet.Migrator.Rollbacks.{migration}.sql")!;
        return new StreamReader(stream).ReadToEnd();
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var entryPoint = typeof(Program).Assembly.EntryPoint!;
        var result = entryPoint.Invoke(null, [args]);
        return result is Task<int> task ? await task : (int)result!;
    }
}
