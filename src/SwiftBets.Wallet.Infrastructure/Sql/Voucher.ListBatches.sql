SELECT TOP (@Limit) BatchId, RequestKey, Count, Amount, Currency, Source, Note, CreatedBy, CreatedAt FROM wallet.VoucherBatches ORDER BY CreatedAt DESC;
