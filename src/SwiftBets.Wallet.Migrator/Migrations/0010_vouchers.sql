-- Cash vouchers (D168). A liability account per currency holds what open vouchers are worth; codes are stored only as hashes.
INSERT INTO wallet.Accounts (AccountId, Kind, Currency, CreatedAt)
SELECT v.AccountId, 5, v.Currency, SYSUTCDATETIME()
FROM (VALUES ('00000000-0000-0000-0000-00000000d001', 'ZAR'),
             ('00000000-0000-0000-0000-00000000d840', 'USD')) v (AccountId, Currency)
WHERE NOT EXISTS (SELECT 1 FROM wallet.Accounts a WHERE a.AccountId = v.AccountId);

CREATE TABLE wallet.VoucherBatches
(
    BatchId    uniqueidentifier  NOT NULL CONSTRAINT PK_VoucherBatches PRIMARY KEY,
    RequestKey nvarchar(200)     NOT NULL CONSTRAINT UQ_VoucherBatches_RequestKey UNIQUE,
    Count      int               NOT NULL CONSTRAINT CK_VoucherBatches_Count CHECK (Count > 0),
    Amount     bigint            NOT NULL CONSTRAINT CK_VoucherBatches_Amount CHECK (Amount > 0),
    Currency   char(3)           NOT NULL,
    Source     tinyint           NOT NULL,
    Note       nvarchar(200)     NOT NULL,
    CreatedBy  nvarchar(100)     NOT NULL,
    CreatedAt  datetimeoffset(3) NOT NULL
);

-- Status: 1 active, 2 redeemed, 3 voided. Source: 1 bought from a balance, 2 retail, 3 promotion.
CREATE TABLE wallet.Vouchers
(
    VoucherId       uniqueidentifier  NOT NULL CONSTRAINT PK_Vouchers PRIMARY KEY,
    IssueKey        nvarchar(200)     NOT NULL CONSTRAINT UQ_Vouchers_IssueKey UNIQUE,
    CodeHash        binary(32)        NOT NULL CONSTRAINT UQ_Vouchers_CodeHash UNIQUE,
    Last4           char(4)           NOT NULL,
    Amount          bigint            NOT NULL CONSTRAINT CK_Vouchers_Amount CHECK (Amount > 0),
    Currency        char(3)           NOT NULL,
    Source          tinyint           NOT NULL,
    SourceAccountId uniqueidentifier  NOT NULL CONSTRAINT FK_Vouchers_SourceAccount REFERENCES wallet.Accounts (AccountId),
    PurchasedBy     uniqueidentifier  NULL,
    BatchId         uniqueidentifier  NULL CONSTRAINT FK_Vouchers_Batch REFERENCES wallet.VoucherBatches (BatchId),
    RecipientEmail  nvarchar(254)     NULL,
    Status          tinyint           NOT NULL,
    CreatedAt       datetimeoffset(3) NOT NULL,
    ExpiresAt       datetimeoffset(3) NOT NULL,
    RedeemedBy      uniqueidentifier  NULL,
    RedeemedAt      datetimeoffset(3) NULL,
    VoidedBy        nvarchar(100)     NULL,
    VoidReason      nvarchar(400)     NULL,
    VoidedAt        datetimeoffset(3) NULL
);
CREATE INDEX IX_Vouchers_PurchasedBy ON wallet.Vouchers (PurchasedBy, CreatedAt) WHERE PurchasedBy IS NOT NULL;
CREATE INDEX IX_Vouchers_RedeemedBy ON wallet.Vouchers (RedeemedBy, RedeemedAt) WHERE RedeemedBy IS NOT NULL;
CREATE INDEX IX_Vouchers_Batch ON wallet.Vouchers (BatchId) WHERE BatchId IS NOT NULL;
CREATE INDEX IX_Vouchers_Last4 ON wallet.Vouchers (Last4);
CREATE INDEX IX_Vouchers_Status ON wallet.Vouchers (Status, Currency) INCLUDE (Amount);

-- Failed redeems per customer ("user:<id>") and per hashed address ("ip:<hash>") in a rolling window, and any lockout.
CREATE TABLE wallet.VoucherAttempts
(
    Subject     varchar(100)      NOT NULL CONSTRAINT PK_VoucherAttempts PRIMARY KEY,
    Failures    int               NOT NULL,
    WindowStart datetimeoffset(3) NOT NULL,
    LockedUntil datetimeoffset(3) NULL
);
