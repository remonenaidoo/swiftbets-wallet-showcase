-- Rolls back 0011_statement_balances. The columns are copies (sequence, date and running balances), so nothing is lost.
DROP INDEX IF EXISTS IX_LedgerEntries_Account_PostedAt ON wallet.LedgerEntries;
DROP INDEX IF EXISTS IX_LedgerEntries_Account_Sequence ON wallet.LedgerEntries;
ALTER TABLE wallet.LedgerEntries DROP COLUMN IF EXISTS Sequence, PostedAt, AvailableAfter, BonusAfter;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Wallet.Migrator.Migrations.0011_statement_balances.sql';
