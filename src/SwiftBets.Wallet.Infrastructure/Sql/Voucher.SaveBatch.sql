-- Insert-or-get by request key; the range lock makes a concurrent repeat wait and then read the first one's row.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM wallet.VoucherBatches WITH (UPDLOCK, HOLDLOCK) WHERE RequestKey = @RequestKey)
    INSERT INTO wallet.VoucherBatches (BatchId, RequestKey, Count, Amount, Currency, Source, Note, CreatedBy, CreatedAt)
    VALUES (@BatchId, @RequestKey, @Count, @Amount, @Currency, @Source, @Note, @CreatedBy, @CreatedAt);
SELECT BatchId, RequestKey, Count, Amount, Currency, Source, Note, CreatedBy, CreatedAt FROM wallet.VoucherBatches WHERE RequestKey = @RequestKey;
COMMIT;
