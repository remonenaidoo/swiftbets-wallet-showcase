SELECT BatchId, RequestKey, Count, Amount, Currency, Source, Note, CreatedBy, CreatedAt FROM wallet.VoucherBatches WHERE BatchId = @BatchId;
