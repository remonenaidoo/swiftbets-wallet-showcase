-- Each voucher liability account against the vouchers still open in its currency (D168).
SELECT TOP (@Limit) CAST(7 AS tinyint) AS Kind, a.AccountId, CAST(NULL AS uniqueidentifier) AS PostingId,
       COALESCE(v.Outstanding, 0) AS Expected, COALESCE(e.Total, 0) AS Actual
FROM wallet.Accounts a
LEFT JOIN (SELECT Currency, SUM(Amount) AS Outstanding FROM wallet.Vouchers WHERE Status = 1 GROUP BY Currency) v ON v.Currency = a.Currency
LEFT JOIN (SELECT AccountId, SUM(Amount) AS Total FROM wallet.LedgerEntries GROUP BY AccountId) e ON e.AccountId = a.AccountId
WHERE a.Kind = 5 AND COALESCE(v.Outstanding, 0) <> COALESCE(e.Total, 0)
ORDER BY a.AccountId;
