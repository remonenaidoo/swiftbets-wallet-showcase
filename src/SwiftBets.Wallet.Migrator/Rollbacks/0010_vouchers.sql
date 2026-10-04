-- Rolls back 0010_vouchers. Refuses while any voucher exists: its money and its code would be lost.
IF OBJECT_ID('wallet.Vouchers') IS NOT NULL AND EXISTS (SELECT 1 FROM wallet.Vouchers)
    THROW 50001, 'Vouchers exist; they hold customer money and cannot be dropped.', 1;
DROP TABLE IF EXISTS wallet.VoucherAttempts;
DROP TABLE IF EXISTS wallet.Vouchers;
DROP TABLE IF EXISTS wallet.VoucherBatches;
DELETE a FROM wallet.Accounts a WHERE a.Kind = 5 AND NOT EXISTS (SELECT 1 FROM wallet.LedgerEntries e WHERE e.AccountId = a.AccountId);
